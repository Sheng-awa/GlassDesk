# GlassDesk.Platform API

All Windows-facing production objects are created on the existing WinUI UI thread. Its `IUiDispatcher.InvokeAsync<T>` must install/preserve the WinUI `SynchronizationContext` through asynchronous continuations. Dispose observers/system integration/media before destroying the native host or closing the dispatcher queue. This library does not alter system display scaling, SMTC or music playback at startup.

## Media

`NetEaseMediaAdapter(IUiDispatcher dispatcher, Diagnostics? diagnostics = null)` implements `IMediaAdapter`; call `await StartAsync()` once. `Current` and `SnapshotChanged` expose real session properties. Matching is exact case-insensitive `cloudmusic.exe`; browser/current-global-session changes cannot redirect control. Existing matching binding is preserved; if multiple matching sessions exist, prefer a playing NetEase session then the first exact match. Closed sessions are excluded.

Manager/session calls, subscriptions, commands and continuations remain on the creating UI/COM thread. Native event callbacks enqueue work onto that owner. Metadata is coalesced and session generation/content version gate every asynchronous result; later metadata cancels preceding artwork work. Images decode to PNG with longest edge at most 768px, and the adapter keeps only three cached images in memory. Covers/track strings never enter persistent diagnostics or preferences.

`ExecuteAsync(MediaCommand, string sessionToken, TimeSpan? position, CancellationToken)` checks the live binding and actual source capabilities, serializes commands and returns `CommandResult(Accepted, Verified, Error)`. Replacement sessions reject old tokens. Toggle selects explicit play/pause when available. Seek requires a valid timeline and clamps `TimeSpan.Ticks` to the source range. Requests have an 8-second operation limit and 12-second overall command limit; queue acquisition is bounded to five seconds. Accepted requests get bounded actual-state readback, and unobserved transitions report `Verified=false`. Verification polling exists only during a requested command; resting media updates are event-driven. Call `Refresh()` on resume to re-enumerate the binding and re-sync the timeline. `StartAsync()` is idempotent once the broker exists; it does not substitute for a resume refresh. A broker startup failure is a disconnected snapshot with a notice; calling `StartAsync()` again can retry.

## Preferences and diagnostics

`PreferenceStore(string? directoryPath=null)` defaults to `%LocalAppData%/GlassDesk`; `Load()` returns version-1 preferences; `TakeLoadNotice()` consumes one corruption/read-failure notice. Invalid input is backed up to `settings.corrupt-<timestamp>-<unique>.json`; usable defaults remain. `SaveAsync(preferences, cancellationToken)` serializes writes, flushes a same-directory staging file, then atomically replaces `settings.json`. Numeric validation handles non-finite input. Explicit directory overrides isolate verification from the real profile. Dispose after pending saves finish.

`Diagnostics.RecordError(component, exception)` records only exception type/HRESULT, never its message. `RecordWin32Error(component,error)`, `RecordEvent(component,fixedEventCode)`, `RecordCapability(connected,capabilities)` maintain a 128-entry in-memory ring. Components/event codes must be fixed developer constants, never source content. `ExportAsync(destinationPath)` writes system/app/runtime, exact media source, current capability flags and errors. No automatic history-file logging is enabled.

## Tray, startup, hotkey and system messages

`SystemIntegration(nint hostHandle, IUiDispatcher dispatcher, Diagnostics? diagnostics=null)` installs an owned `SetWindowSubclass` handler and adds one tray icon. The existing HWND remains the native agent's window; subclass dispatch continues via `DefSubclassProc`. Construction, hotkey/startup operations and `Dispose()` require the HWND UI thread. `Dispose()` unregisters hotkey, removes the icon/window marker and only this subclass.

Events (all enqueued on the UI dispatcher): `ShowHideRequested`, `SettingsRequested`, `OpenMusicRequested`, `ResetPositionRequested`, `QuitRequested`, `DisplayChanged`, `Resumed`, `SystemSettingsChanged`. `WidgetVisible` updates the tray's show/hide label. The tray offers these five product commands; selecting the icon opens settings. `TaskbarCreated` rebuilds the icon once after Explorer restart. `WM_DISPLAYCHANGE`, resume notifications, `WM_SYSCOLORCHANGE`, theme/settings/DWM and power-status changes trigger the corresponding callbacks. The widget is never explicitly foregrounded for tray actions.

`TrySetHotkey(uint modifiers,uint key,out string? error)` uses Win32 modifier bits Alt=1/Ctrl=2/Shift=4/Win=8 and a virtual key; key=0 clears. The default is unbound. It first registers a replacement under another ID, then removes the old binding, so conflicts preserve the previous key. No-repeat is added internally. Save preferences only after registration succeeds.

`SetStartupEnabled(bool enabled,out string? error)` writes only the current user's `Run/GlassDesk` value pointing at the actual `GlassDesk.exe --startup`, or removes that own value. Call only when a user changes the setting or restores a previously explicit saved true preference. Do not invoke merely to apply a new profile's default false. This function has not been called during platform development tests.

`OpenNetEase(out string? error)` is an explicit user command only. It uses NetEase's own `orpheus://` protocol, then registered `App Paths/cloudmusic.exe` fallback. Widget startup never calls it. Playback is not initiated by GlassDesk.

`ReadMaterialEnvironment()` returns `MaterialEnvironment(HighContrast,TransparencyEnabled,EnergySaver,AnimationsEnabled)`; `UseSolid`/`ReduceMotion` derive system fallback state. Reads only the user's existing system preferences.

## Single instance

Create and keep `SingleInstanceGate` on the startup/UI thread. `IsPrimary` owns a session-local mutex, including abandoned-owner recovery. A secondary process calls `RequestExistingSettings()` and exits. Installer/`--quit` calls `RequestQuit()` then exits; primary receives the matching registered message as `SettingsRequested`/`QuitRequested`. Both synchronously retry for at most two seconds while the first instance installs its HWND marker, then return whether a marked resident HWND acknowledged the message. Use them from secondary startup/installer processes, where no resident UI is running. Only the GlassDesk window property is searched, so unrelated programs do not receive control messages. Dispose the gate on its acquiring thread after other app shutdown.

## Monitors and external scenes

Create `var monitorService = new MonitorService()`; its instance method `monitorService.GetMonitors()` returns `IReadOnlyList<MonitorDescriptor>` with physical bounds/workarea, primary flag and effective DPI scale. Monitor identity uses `EnumDisplayDevices(EDD_GET_DEVICE_INTERFACE_NAME)` and falls back to the display device label when Windows supplies no interface identity. For each enumeration, a short-lived hidden/no-activate PerMonitorV2 probe window at the monitor center obtains `GetDpiForWindow`, then is destroyed immediately; `GetScaleFactorForMonitor` is the fallback. Temporary thread DPI awareness is restored in `finally`. This query never changes global display scaling. Static helpers `MonitorService.SelectMonitor(monitors,storedId)` return the remembered monitor, otherwise primary, and `MonitorService.RestoreAnchor(monitor,prefs,widthDip=232,heightDip=48)` convert normalized horizontal center and DIP vertical offset into a fully clamped physical placement, including negative monitor origins.

`ForegroundObserver(IUiDispatcher,Diagnostics?)`: `Start()`, `Refresh()`, `Current`, `SceneChanged`, `Dispose()`. Out-of-context WinEvent hooks monitor external foreground, move/resize, minimize, visibility/destroy changes and coalesce callbacks. Own-process HWNDs and desktop/shell surfaces are excluded; opening own settings preserves the previous external scene. Normal captioned maximized apps retain a maximized scene; borderless monitor-filling app windows report fullscreen. Fullscreen detection covers ordinary/borderless video/apps, not exclusive-fullscreen graphics injection. No idle polling timer is installed.

## Verification

`dotnet run --project tests/GlassDesk.PlatformChecks -c Release` exercises isolated preference defaults/atomic writes/cancellation/corrupt backup/version validation, monitor restoration/migration/clamping, normal-maximized versus fullscreen geometry, and bounded diagnostic privacy. This test intentionally performs no GUI automation and no real-profile/startup/hotkey/media mutation. Full source events, actual playback controls, tray menus, shortcuts, Explorer restart and sleep/resume require primary-agent integrated acceptance through cua-driver.

The isolated verification passed 23 checks on 2026-10-12; Release build completed with zero warnings/errors. Reports are under `artifacts/platform-checks/<run-id>/checks-result.json`. These checks validate persistence, geometry and diagnostic logic; they do not establish real-device media/tray/hotkey/resume acceptance.

Official API references: [GSMTC](https://learn.microsoft.com/en-us/uwp/api/windows.media.control), [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook), [SetWindowSubclass](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-setwindowsubclass), [Shell_NotifyIcon](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw), [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey).
