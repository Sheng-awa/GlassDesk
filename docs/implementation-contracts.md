# Implementation boundaries for 0.1.0

All source is original in GlassDesk. Shared models live in `src/GlassDesk.Core/Contracts.cs`; do not change public signatures without coordinating with the primary agent.

- Core controller and final integration belong to the primary agent.
- `native/GlassDesk.Native`, `src/GlassDesk.NativeBridge`, `scripts/build-native.ps1`: HWND, WUC backdrop, region, layered pass-through shadow, generic D2D surface renderer, UIA. No form/media business decisions.
- `src/GlassDesk.App`: WinUI settings and C# widget scenes. Uses native bridge's Windows.UI.Composition compositor/root for the widget; Microsoft.UI.Composition belongs only to settings. Application bootstrap and final orchestration are primary-agent integration.
- `src/GlassDesk.Platform`: exact NetEase adapter, preference store, startup/hotkey/tray, monitor and foreground observers. No GUI automation.

## Native bridge API

`NativeWidgetHost : IWidgetHost` constructor runs on the existing WinUI UI/STA thread. It owns a Windows.System dispatcher queue distinct from the WinUI dispatcher. It exposes `Windows.UI.Composition.Compositor Compositor` and `ContainerVisual ContentRoot` to C#; exports ABI pointers with AddRef and bridges them using CsWinRT. ContentRoot is above the native background; do not pass these objects to WinUI composition APIs.

`CompositionDrawingSurface RenderSurface(float widthDip, float heightDip, IReadOnlyList<SurfaceOp> operations)` returns a new surface owned by C#. Rendering uses generic D2D drawing commands; layout and text are supplied by the C# scene.

`SurfaceOp` native bridge model: `Kind` enum Rectangle/RoundedRectangle/Text/Image/Line/Ellipse, `X,Y,Width,Height,Radius,FontSize` floats in DIP, `Color` uint ARGB, optional `Text` UTF-16, optional `Image` bytes, `Bold` bool, `Center` bool. Font is the system Segoe UI family; grayscale text AA for transparency. Image decoding must bound longest edge to 768 and keep only three decoded bitmaps. Surface API must use CompositionDrawingSurfaceInterop.BeginDraw/EndDraw and a D3D/D2D graphics device, handle device-loss recovery.

HostFrame size/radius/material changes update background, visual clip, HWND region and shadow together. C# owns frame animation and position. Input forwards every pointer event to C# on the owning UI thread. Host must preserve the down-event screen coordinate (not sample a later cursor position). All main/auxiliary windows are noactivate, toolwindow and topmost. Shadow is a layered WS_EX_TRANSPARENT window to allow clicks through to another process. Exiting releases HWND, shadow, UIA provider lifetime, D2D/D3D/composition and Windows.System queue.

UIA controls are updated from AccessibleControl. Root must expose a widget name and children with correct enabled state, Invoke, and RangeValue when seek is enabled. UIA commands from other COM threads are posted back to the owning window; a provider must not retain a dangling host pointer after shutdown.

## C# widget scene

`WidgetScene(NativeWidgetHost host)` exposes `Render(WidgetForm form,MediaSnapshot media,DateTimeOffset now,WidgetPreferences preferences,string? notice=null)` and `SetTransitionOpacity(float opacity)` plus `Dispose()`. All layout uses DIP sizes from the plan; image and text surfaces are attached to WUC visuals. Buttons get separate hit rectangles and matching AccessibleControls. Scene exposes `HitTest(float x,float y)` returning a WidgetActions ID or Body. Details persists; the primary controller owns all transitions/timers. Build content only on form/content/timestamp changes, not on every animation frame. Use actual local time when disconnected; absent timeline has an explicit unavailable label and disabled seek. Artist/title support Unicode. Artwork uses an original placeholder when absent. Do not include song data or art in installer assets.

`SettingsWindow` accepts an `ISettingsCoordinator` (defined by App) with current preferences/media/status and callbacks for update preferences, reset position, open music, show/hide, export diagnostics, quit. Settings must use a light WinUI theme, accessible controls, scrollable four settings groups and short first-run guidance. Close hides; resident host remains. Hotkey conflict keeps prior binding. Primary agent provides coordinator implementation.

## Platform API

`NetEaseMediaAdapter(IUiDispatcher dispatcher)` implements IMediaAdapter. Exact source is case-insensitive `cloudmusic.exe` only. Session identity and content version gate all asynchronous art/metadata results. Commands execute on the COM owner thread and verify actual subsequent state. CanSeek false or invalid timeline must not show deterministic progress.

`PreferenceStore` loads/saves `%LocalAppData%/GlassDesk/settings.json` with atomic replace, version, corruption backup and one-time notice. It exposes preferences + optional load notice; persistence never stores song history. `Diagnostics` logs errors/system/capabilities only and exports a reviewable file.

`SystemIntegration` owns tray, hotkey, startup and foreground hooks, runs on the UI thread and uses native host HWND for message routing. Provide its exact public surface in `docs/platform-api.md` for primary integration. Tray: show/hide, settings, open music, reset, quit. Default startup off/hotkey none. Do not apply startup enable unless an actual user preference changes. Observe TaskbarCreated, session resume and display changes; rebuild tray once. Single instance can use a Local mutex and a registered message to the main host to open settings/quit.

`MonitorService.GetMonitors()` returns stable monitor IDs and physical bounds/workarea/scale; disappearing monitors migrate to primary and clamp placement. `ForegroundObserver` excludes all own-process windows and desktop/shell surfaces; emits SystemScene on foreground/geometry changes. No static 60fps or busy polling while idle.

Production projects use net10.0-windows10.0.26100.0, min22621, x64. Windows App SDK 2.5.1 locked, unpackaged self-contained, no trimming/singlefile. P0 fixtures are isolated from production startup.
