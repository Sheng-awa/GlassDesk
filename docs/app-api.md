# App UI contracts

`src/GlassDesk.App` contains the original WUC widget scene and a reusable, light WinUI settings window. All methods below run on the WinUI STA/UI thread. `Program.cs`, `App.cs`, and `GlassDeskCoordinator.cs` are supplied by primary integration. The project builds the production `GlassDesk.exe`, disables XAML-generated Main, uses an asInvoker/PerMonitorV2 manifest, targets minimum 22621, pins WinAppSDK 2.5.1, and publishes unpackaged self-contained without trimming/single-file.

## WidgetScene

Construct with `new WidgetScene(NativeWidgetHost host)`. The scene uses the host's **Windows.UI.Composition** compositor and inserts its own container into `ContentRoot`. It never exchanges composition objects with WinUI. The host owns the material, root scale, clip/region and shadow; the scene owns and disposes all its content visuals, brushes and returned drawing surfaces.

- `SizeFor(form)` returns the specified DIP dimensions: 232×48, 360×132, 360×464.
- `SetDetailsViewport(heightDip)` constrains Details to 220..464 DIP. Use `DetailsHeight` for the corresponding native/controller target height; `CurrentSize` gives the active scene size. `ScrollDetails(deltaDip)` moves the clipped artwork/text body and returns whether scrolling is available. Route wheel as `-wheelDelta / 120 * 40` DIP. The close button, timeline and transport footer stay fixed and reachable.
- `Render(form, media, now, preferences, notice)` renders actual content or the local clock. It caches metadata/artwork separately from the narrow Details timeline surface. Call on form, metadata/state/capability, notice, clock or preference changes. All three clock forms display `HH:mm` and use a minute-level content key. The coordinator can sample time once per second without repainting an unchanged minute. Animation frames must not call Render.
- `ApplyPresentation(WidgetPose)` applies the current shell size, whole-scene opacity, content-layer weights, artwork geometry and clip. Content opacity follows the same presented geometry as the interruptible shell motion. Retargeting captures the current weights, artwork position and clip, so content does not run a separate fade toward a final layout inside an unfinished shell. It also updates control rectangles to the currently presented horizontal offset and visible bounds.
- `UpdateTimeline(media, Stopwatch.GetTimestamp())` is the Details-only 250ms update hook. Other forms do no timeline drawing. Missing/invalid time is explicitly labelled unavailable.
- `PositionAt(media, monotonicNow)` estimates a valid source position with monotonic elapsed time and stops advancing when paused.
- `HitTest(dipX,dipY)` returns a `WidgetActions` ID from the latest presented control rectangles. Disabled control regions still return their control ID: the controller must check its capability and consume the action, never reinterpret it as Body. UIA uses the same DIP rectangles. Controls are omitted until the active layer has visible weight and are clipped to the presented shell bounds.
- `GetAccessibleControls()` returns the latest controls; Render and ApplyPresentation send them to the host when they change. The body comes first, specific controls last. Seek RangeValue is a percentage from 0 through 100 and is disabled without both a valid time and source seek capability.
- `SeekPositionFor(dipX)` converts the presented Details seek region into source ticks, accounting for the animated horizontal offset and clamping to the source seek range, or returns null. Its settled local region is x=24..336 DIP. `SetSeekPreview(TimeSpan?)` updates only the local display; release must submit one request through the media adapter and clear the preview.
- `SetPointerFeedback(actionId, pressed)` draws a small hover/press overlay. Each transport button owns an independent surface, so its glyph and background shrink together to 95% over 90ms and return over 150ms. The overlay has a separate 80ms scale response. The coordinator retains the pressed state during pointer movement over the original action and clears it on release or capture loss. Use -1/Body to clear feedback.
- Dispose the scene **before** disposing the native host. DPI changes recreate surfaces from DIP layout; a window size animation does not recreate them.

Artwork is a shared surface whose position, size and rounded clip follow the same presentation as the shell. A replacement stays opaque under the outgoing cover, which fades out over 160ms. A single 180ms completion timer defers overlapping cover updates and renders the latest snapshot after the current blend. Ordinary form/state redraws do not restart the cover fade. Disconnecting, reduced motion, drawing invalidation and disposal stop that timer as appropriate; this is not a continuous artwork refresh loop.

`GraphicsDeviceRestored` invalidates content/timeline caches and recreates surfaces without needing a metadata or DPI change. Drawing invalidation keeps the transition identity, weights and artwork path. Layers still fading out are redrawn along with the target layer, and old drawing surfaces are detached and disposed after each replacement. Text regions use a subtle white backing when the outer glass is transparent enough to reduce legibility; high contrast uses system window/text colors. The playing capsule status dot uses a small WUC scale animation and stops when paused or reduced motion is requested; it represents playback state, not an audio spectrum.

Enabling reduced motion during an active transition immediately settles `MotionController` at its target before considering an unchanged target. The scene then settles its content/artwork presentation, cancels cover completion work and stops transport/feedback/status animations. These are implementation contracts; actual pointer continuity, perceived animation quality and device-loss behavior remain subject to the integrated acceptance report.

Drawing failures invalidate content, timeline, accessibility-name and pointer-feedback caches before propagating to the coordinator's recovery boundary. `RenderingFailed` reports exceptions from the deferred cover callback through that same boundary, rather than waiting indefinitely for another content event. A failed brush assignment disposes the new surface and retains ownership of the prior surface. Feedback keeps the requested pointer state marked dirty until drawing succeeds, so an unchanged hover can retry. Temporary composition animations are disposed even if starting them fails. Direct timeline/scroll/feedback/seek-preview callers must use the same bounded recovery boundary as full Render; recovery callbacks run on the native HWND owner thread, after the native drawing call has returned, and the bridge contains subscriber exceptions before they can cross its WndProc.

## SettingsWindow and coordinator

`new SettingsWindow(ISettingsCoordinator)` creates one reusable window, initially not shown. `Show()` activates this normal settings window. `Hide()` leaves the native resident host alive. The caption close button is cancelled and hides. `CloseForShutdown()` or `Dispose()` closes it during full app exit and unsubscribes events.

Before coordinator exit from tray/hotkey, await `settings.FlushPendingAsync()` to commit the last coalesced slider change. The settings Exit button already does this. Position values are refreshed from the coordinator before each settings request so moving the widget cannot be overwritten by a pending appearance change.

`ISettingsCoordinator` exposes:

```csharp
WidgetPreferences Preferences { get; }
MediaSnapshot Media { get; }
string Status { get; }
string? ConnectionIssue { get; }
bool IsWidgetVisible { get; }
event Action? Changed;
Task<SettingsUpdateResult> UpdatePreferencesAsync(WidgetPreferences preferences);
void ResetPosition();
void OpenMusic();
Task RetryMediaAsync();
void ToggleVisibility();
Task<string> ExportDiagnosticsAsync();
void Quit();
```

`SettingsUpdateResult` has `bool Success` and optional `string Message`. Apply changes to the host immediately, validate/register a changed hotkey before replacing the previous binding, and save preferences atomically. A conflict must return failure and leave `Preferences` with the prior binding; the UI refreshes from that result. Startup is changed only in response to the actual preferences request. The window coalesces fast slider updates for 80ms, serializes requests and preserves the latest pending changes while awaiting the prior update.

Raise `Changed` on the UI thread when connection, preferences, visibility or status changes. `ExportDiagnosticsAsync` returns the saved file's absolute path and must exclude song history/account data. The window displays a copyable path. `OpenMusic` is invoked only by an explicit button click.

`ConnectionIssue` exposes the current initialization or source notice. `RetryMediaAsync()` retries the Windows media connection from the settings action. Startup initializes the optional QA recorder in its own guarded block even when connection initialization fails; a disconnected local-clock session can still record normal resource and pointer evidence. QA form cycles remain opt-in through the launch arguments.

The settings window has four scrollable groups, first-run guidance, normal WinUI accessibility and light Mica. It sizes itself within the current workarea using the actual window DPI. Hotkey capture reads modifier state only while its input box has keyboard focus; it does not install a keyboard hook or modify global system settings.

The window assigns its completed XAML content before connecting `MicaBackdrop`, matching Microsoft's [Mica initialization example](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.micabackdrop). This also keeps the XAML root available while the backdrop subscribes to theme changes.

## WinUI application resources

`App.xaml` contains the application-wide `XamlControlsResources` dictionary and requests the light theme. The generated partial `ResidentApplication` implements WinUI XAML metadata, and its constructor calls the generated `InitializeComponent()` before any window is created. The programmatic settings tree uses those shared resources rather than activating a fresh controls dictionary per window. This follows Microsoft's [application resource guidance](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-resource-dictionary); XAML compilation and PRI generation remain part of the normal WinUI project build.

If settings construction fails, its timer and subscriptions are stopped, its content/backdrop are detached, and the partially created window is closed before the original exception is rethrown. The primary coordinator records that exception and can report the failure without treating an unopened guide as acknowledged.

The first controls dictionary activation failed with `0x80004005` before standard application initialization was added. A cold Cua launch of the later content-first build displayed the complete settings window, exposed 48 UIA nodes including first-run guidance, and remained alive through a 30-second residency sample (`artifacts/qa/resident-backdrop-order.json`). That test exercised the complete native host, scene and coordinator from a build directory; it did not establish that the publish directory contained the same application resources. The native `0xC000027B` crash alone does not identify backdrop ordering as its cause.

The subsequent published application still crashed when settings were first opened because unpackaged `dotnet publish` omitted `GlassDesk.pri` and `App.xbf`, although both existed in the successful build directory. The application PRI embeds `ms-resource://GlassDesk/Files/App.xbf` and the merged WinUI resources. The unpackaged MRT Core publish omission matches the primary [WindowsAppSDK issue #6720](https://github.com/microsoft/WindowsAppSDK/issues/6720). `PublishGlassDeskXamlResources` now adds the application PRI and every `GeneratedXamlDest` item to `ResolvedFileToPublish`, preserving nested relative paths. Publishing fails if required application resources are absent, and the final output is checked again after Publish.

The resource-only Cua comparison preserved the hashes of all 582 original production files and added only the PRI/XBF (584 files total). Its first settings display exposed 48 UIA nodes; guide acknowledgement persisted, closing settings left the widget resident, a second launch reopened settings in the same process with 44 nodes, and full exit removed the process and its windows. A separate source copy started with no `obj` or `bin`, passed locked restore and Release publish, and included matching PRI/XBF hashes plus a test-only nested `Validation/PublishProbe.xbf`. Both XAML files were embedded in its PRI. See `artifacts/qa/resources-only-baseline-manifest.json` and `artifacts/qa/fresh-publish-resources.json`. The probe exists only in that validation copy; production contains the original application XAML.

For diagnosis only, the internal overload `SettingsWindow(coordinator, minimalContent, useBackdrop, trace)` can realize a plain Grid/TextBlock/Button window or disable the settings backdrop. The public constructor always uses the full settings tree and Mica. The optional trace records construction, content assignment, backdrop connection, activation, content Loaded, and first completed layout; tracing is detached on teardown and never changes saved preferences.
