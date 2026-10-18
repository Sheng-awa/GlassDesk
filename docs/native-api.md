# Native host and bridge, 0.1.0

The native code owns HWNDs, Windows.UI.Composition interop, operating-system input and generic drawing. C# owns the widget's forms, media state, layout and animation. All code is original inside GlassDesk. Native composition objects belong to Windows.UI.Composition; they must not be used with WinUI's Microsoft.UI.Composition objects.

## Managed API

`new NativeWidgetHost()` runs on the existing application UI STA. It creates a hidden 232 × 48 DIP host with a 24 DIP radius. The coordinator renders content, sets placement, then explicitly shows it. It implements the frozen `IWidgetHost` contract:

```csharp
event Action<HostPointerEvent>? PointerEvent;
event Action<HostContext>? WindowContextChanged;
event Action<string>? NativeError;
event Action? GraphicsDeviceRestored;
HostContext Context { get; }
nint Handle { get; }
Compositor Compositor { get; }
ContainerVisual ContentRoot { get; }
void ApplyFrame(HostFrame frame);
void SetPlacement(int screenX, int screenY);
void SetVisible(bool visible);
void SetAccessibleControls(IReadOnlyList<AccessibleControl> controls, string name);
CompositionDrawingSurface RenderSurface(float widthDip, float heightDip,
    IReadOnlyList<SurfaceOp> operations);
void Dispose();
```

Every host/composition method runs on the thread that created the host. Other threads are rejected. The native callback runs on that same thread. `WindowContextChanged` is deduplicated by the managed bridge and covers placement, dimensions and DPI. Pointer events retain the coordinates from their actual messages; the down coordinate is never replaced with a later cursor sample. Native capture supplies moves/releases during drag and sends CaptureLost on an external capture change. Form selection, drag thresholds, control behavior and state transitions remain in Core.

`ContentRoot` is expressed in DIP. Native assigns its `Scale = dpi / 96` and its `Size` to the current HostFrame. The native background and visual clip use physical pixels. C# creates a surface brush with `Stretch.Fill` and sets its sprite's size to the intended DIP width/height. `RenderSurface` allocates the corresponding physical pixel surface and renders DIP commands at the current DPI. Recreate content when DPI changes. A drawing surface is owned by the scene that receives it; close it when replaced or when the scene is disposed.

`SurfaceOpKind` contains `Rectangle`, `RoundedRectangle`, `Text`, `Image`, `Line`, `Ellipse`, `Icon`, and `MultilineText`. The immutable object-initializer model has `Kind`, `X`, `Y`, `Width`, `Height`, `Radius`, `FontSize`, `Color`, `Text`, `Image`, `Bold`, `Center`. All dimensions are DIP, and Color is unsigned ARGB. Line width/height are signed endpoint deltas; its Radius is stroke thickness (minimum 0.5 DIP). Images preserve their aspect ratio with a centered cover crop; Image Radius clips that crop to a rounded rectangle. UTF-16 text uses the system Segoe UI Variable Text family and an explicit DirectWrite `IDWriteTextLayout`, grayscale antialiasing, clipping and character ellipsis. Single-line text converts CR/LF/tab to spaces; `MultilineText` wraps within its layout bounds. `Icon` uses Segoe Fluent Icons without ellipsis. Center selects horizontal centering. Native has no content-specific layouts or bundled music art.

## Window, material and DPI behavior

The primary window is a topmost Win32 popup with `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW` and `WS_EX_NOREDIRECTIONBITMAP`. `WM_MOUSEACTIVATE` returns `MA_NOACTIVATE`; showing, placing and resizing it uses no-activation flags. It has no taskbar/Alt+Tab entry. HWND region and WUC rounded geometric clip follow the currently presented frame, including intermediate animation sizes.

The glass uses `DWMWA_USE_HOSTBACKDROPBRUSH`, `CreateHostBackdropBrush`, and a GPU Gaussian blur whose amount is `HostFrame.Blur × dpi / 96`. A subtle neutral vertical tint and a thin inside border concentrate the light response at the upper edge. Tint and border gradient brushes, plus the solid fallback brushes, are created once and reused; material updates change their properties only when the corresponding parameter, DPI-scaled blur or system color changes. The tint's alpha modulation scales with the requested opacity and goes to zero at the fully transparent and fully opaque endpoints.

An original deterministic 64-pixel grain surface repeats with a border effect. Its opaque tile spans 0.42–0.58 grayscale at 1.2% visual opacity, bounding its luminance variation to 0.192% before other layers; this is a parameter bound, not a measured visual result. Its alpha weighting is independent of the effect's texture handling. Solid fallback uses `COLOR_WINDOW` and a `COLOR_WINDOWFRAME` border, allowing the scene's high-contrast text to match system colors. It is applied when requested by HostFrame or when graphics recovery fails.

The shadow is an independent topmost, no-activation, tool window with `WS_EX_LAYERED | WS_EX_TRANSPARENT`. It uses a premultiplied black bitmap with a transparent center and an exterior rounded Gaussian falloff. Its `WM_NCHITTEST` also returns HTTRANSPARENT. It sits immediately behind the primary HWND without an owned-popup Z-order constraint. Position changes reuse the bitmap; geometry/DPI changes recreate it. Duplicate layout notifications do not cause duplicate bitmap uploads. It hides and is destroyed with the host. Click-through and focus behavior still require actual cross-process pointer verification.

Physical geometry is always calculated from the current canonical DIP HostFrame. WM_DPICHANGED uses the DPI from wParam and preserves that frame, rather than scaling an existing pixel rectangle or resetting a form. WM_SETTINGCHANGE/WM_DISPLAYCHANGE reconcile monitor DPI using a newly created, never-shown PMv2 STATIC HWND at the host's monitor; a resident GetDpiForWindow can still hold a previous value during a settings broadcast. A single 300 ms follow-up runs after the broadcast, then its timer stops. This changes no display setting and adds no idle polling loop. The primary coordinator owns anchor/clamping and disconnected-monitor placement policy.

## Graphics and artwork

Drawing uses a BGRA D3D11 device, D2D device and CompositionGraphicsDevice. Hardware device creation falls back to WARP. BeginDraw/EndDraw implement the surface interop; the atlas update offset is included in the D2D transform. Artwork decode uses WIC, limits the longest edge to 768 physical pixels and caches at most the three most recent decoded bitmaps. Compressed bytes are retained only for those entries, bounded to 32 MB per input. Invalid image bytes omit that operation; the C# scene supplies the original placeholder beneath it.

A surface draw that reports device removal/reset or D2D target recreation gets one recovery attempt. Native first selects solid material, clears its device-specific artwork cache, rebuilds D3D/D2D, replaces the graphics device's rendering device and recreates grain. After successful drawing it posts a notification back to the owning HWND. `GraphicsDeviceRestored` therefore runs after the failing RenderSurface call has returned, and the scene must invalidate and redraw existing surfaces even when its form/content key is unchanged. It must unsubscribe during disposal. There is no recursive render callback inside native recovery. Terminal failure returns an HRESULT/COMException; the coordinator must record it, keep the resident app alive and retain the readable fallback. Forced TDR/device-removal behavior has not been tested on hardware.

## Accessibility and ownership

The host exposes a UIA pane/fragment root and children from AccessibleControl. Later controls take priority over a broad body rectangle for point hit testing. Children expose names, enablement, Invoke or RangeValue as appropriate. Disabled range controls are read-only. Commands from COM/UIA threads are posted to the owning HWND; they never call scene or media logic from that COM thread. The callback receives WidgetActions identifiers. Values are revalidated against current controls before dispatch. Keyboard focus remains with the user's application.

Providers retain a shared accessibility state, never a raw Host pointer. Shutdown invalidates the HWND/root under the same mutex used by UIA posting, disconnects the provider, drains owned pending RangeValue allocations and closes both windows. A retained provider returns UIA_E_ELEMENTNOTAVAILABLE. A shared host lifetime spans native dispatches and ABI calls, so synchronous Dispose inside a callback cannot free the WndProc's object prematurely.

Dispose scene surfaces/visuals first, then Platform's HWND subclass, tray/hooks/hotkey, then NativeWidgetHost. The native host releases the target/root, composition material objects, image cache, D2D/D3D/WIC/DirectWrite, layered bitmap/DC and its own Windows.System dispatcher queue. It never shuts down a queue supplied by another owner. Queue shutdown pumps messages with a bounded deadline. Native does not post WM_QUIT or terminate the resident app when a settings window closes. Unhandled messages reach DefWindowProc and cooperate with a Platform SetWindowSubclass chain.

The C ABI is x64 sequential structs declared in `native/GlassDesk.Native/NativeApi.h`; pointers are borrowed only for the call. Exported composition/surface pointers include an AddRef. The managed bridge projects them through CsWinRT and releases the temporary ABI reference. Managed strings/art byte pins are released in finally blocks.

## Build and checks

```powershell
.\scripts\build-native.ps1
dotnet restore .\src\GlassDesk.NativeBridge\GlassDesk.NativeBridge.csproj --locked-mode
dotnet build .\src\GlassDesk.NativeBridge\GlassDesk.NativeBridge.csproj -c Release --no-restore
```

The script selects MSVC x64 and requires Windows SDK 10.0.26100.0. Native compiles with /W4 /WX /MT; the bridge targets net10.0-windows10.0.26100.0, minimum 22621, x64. The native DLL is copied into build/publish output by the bridge. The lock file contains the Core project reference; Windows SDK projections come from the pinned .NET SDK.

The optional fixture is built with `.\scripts\build-native.ps1 -BuildRegression` and run with `.\artifacts\native\Release\GlassDesk.NativeRegression.exe`. It never shows a window, injects pointer input or changes Windows settings. The full Release script builds and runs it by default; `.\scripts\build-release.ps1 -SkipStress` builds only the native DLL and omits the fixture. This mode also passes `--no-stress` to Core checks while keeping Platform checks unchanged.

On 2026-10-12 the fixture passed: six DPI message cases retaining a non-integer animated DIP frame; current-monitor reconciliation; wrong-thread rejection; all six paint operations including signed lines/rounded art; 768-pixel/three-image cache limits; graphics recreation and queued redraw notification; 1,000 hidden geometry changes with GDI objects 2 → 2 and USER objects 7 → 6; synchronous Quit disposal of both HWNDs; safe retained UIA provider disconnection; unchanged foreground. Those historical results do not establish acceptance of the later material/text changes. This is an ABI/message/resource regression, not evidence of actual pointer delivery, global scale switching, presented frame rate, mixed-monitor behavior, hardware device-loss recovery, or 60-minute residency. Those remain separate integrated acceptance checks.
