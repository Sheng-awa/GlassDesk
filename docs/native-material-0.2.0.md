# Native liquid material, 0.2.0

The optional material retains the required route: **WGC monitor texture → GPU ROI crop → original HLSL blur/refraction → native Composition surface**. The ordinary backdrop, tint, border, rounded mask, text, artwork and controls remain separate. Liquid glass is off by default. Capture failure, hidden state, solid/high-contrast policy and device recreation keep the user's enabled intent, stop capture, and expose the current reason while ordinary frosted glass remains available.

## Capture ownership and bounded shutdown

The visible host owns `GlassDesk.CaptureWorker.exe`. It starts only for active, visible liquid glass. The worker uses the same DXGI adapter, its own D3D11 device and a two-buffer free-threaded WGC frame pool. An anonymous mapped control block carries geometry, counters and errors; desktop pixels stay in GPU textures. Only three explicitly listed inherited handles reach the child: the control mapping, stop event and NT shared texture. There is no service, network communication or persistent pixel stream.

The worker retains at most the latest WGC frame and performs `CopySubresourceRegion` for the requested ROI with blur/refraction padding. A keyed mutex transfers GPU ownership. The host copies the delivered ROI into a private crop texture before filtering, then releases the shared texture for the next producer frame. This makes material and raw-background diagnostics refer to the exact same frame. Geometry changes request a fresh crop of the retained frame even when a static desktop produces no new WGC callback. A 34 ms gate limits GPU post-processing to at most 29.4 Hz; it does not cap every part of the OS monitor-capture producer.

The child is created suspended, assigned to a `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` job, and only then resumed. Stop signals the event, waits 100 ms, closes the job as the bounded fallback, waits up to 500 ms, and closes channel handles. Therefore an OS/driver stall inside WGC shutdown cannot leave a capture worker behind. An abrupt host `ExitProcess` test checks this separately from ordinary C++ destructors.

Before capture, both the body HWND and separate shadow HWND receive `WDA_EXCLUDEFROMCAPTURE`, with readback required to equal `0x11`. Their original values are restored on disable, hide, fallback and disposal. The OS capture indication is retained. The active widget can consequently be absent from external screenshots, as agreed in the plan. Failed startup retries at five-second intervals rather than continuously allocating capture sessions.

## Material rendering

An adaptive 1×/2×/4× ROI prefilter followed by horizontal and vertical Gaussian filtering avoids the sparse text bands in the earlier shader. The finite rounded bevel is 8–22 physical pixels wide, bends sampled background by 6–15 pixels, blends some unfiltered same-frame background near the edge, and adds restrained rim and directional light. A neutral interior lift preserves dark-content contrast on dark backgrounds. Zero blur skips filtering while retaining refraction. The shader is original; there is no Qt dependency or copied/translated Qt shader.

`CompositionDrawingSurface` drawing respects the returned atlas offset. Normal rendering never maps desktop pixels to CPU memory. Only explicitly requested one-frame diagnostic output creates temporary staging textures and writes the processed material plus `<path>.background.png`, then releases the staging resources. The existing floating rounded geometry and clear foreground remain unchanged. The integer HWND region retains a physical pixel of antialias coverage; input still uses the floating rounded boundary.

Four reusable timestamp/disjoint query slots report host GPU filter and surface-copy spans without a query-induced wait. These spans exclude the worker's complete capture cost and are not GPU engine busy time. `LiquidGlassStats` texture bytes are an estimate, not Windows process GPU allocation counters. CPU, private bytes, process GPU memory, worker resources and DWM counters are recorded separately during production acceptance.

## Lifecycle investigation and evidence

The former in-process WGC candidate accumulated about 2 GiB of Windows shared GPU allocations after 60 GUI cycles. Closing the WinRT device, clearing/flushing its immediate context, separating the capture device and reusing a frame pool did not resolve that system-counter trend. A flat `QueryVideoMemoryInfo` result alone had incorrectly looked reassuring; it is not the final success criterion. These failed attempts remain under `artifacts/final-acceptance` as investigation records.

The final job-owned worker implementation passed two native 100-cycle real-WGC runs. Each cycle verifies actual received frames, exact body/shadow affinity restoration, loss of the material surface when disabled, and termination of the specific worker process. The second run also recorded 52 Windows GPU counter samples: combined dedicated/shared usage peaked at 34.33 MiB and ended at 17.92 MiB in that hidden native fixture; these are fixture measurements, not the visible application's budget. `--capture-owner-exit` verified the worker disappeared after abrupt owner exit. The final full Release build also passed native ABI, six simulated DPI changes, 1,000 geometry changes and hidden lifetime checks. Simulated DPI checks do not replace physical mixed-DPI testing.

Commands:

- `GlassDesk.NativeRegression.exe --liquid-hidden <absolute-PNG-path>`: actual monitor capture and GPU material/raw comparison, without showing test windows.
- `GlassDesk.NativeRegression.exe --liquid-cycles 100`: real capture and worker shutdown on every cycle, with exact affinity readback.
- `GlassDesk.NativeRegression.exe --capture-owner-exit`: abrupt owner exit; the external process check verifies the emitted worker PID has terminated.
- `scripts/check-liquid-gui.ps1`: cua-driver snapshot/action/readback against the actual installed settings page. Production cycle results and continuous resource measurements are summarized in the [acceptance report](test-report.md).

Production appearance, fullscreen/input behavior, real background changes, installed application resources and residency must be judged from that report. Native hidden fixtures do not prove the visible product's appearance or 60 FPS. Actual hardware removal, sleep/monitor-loss recovery and mixed-DPI devices require the corresponding environment and remain explicitly unverified when unavailable.

## Primary references

- [Microsoft WGC overview](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture).
- [Monitor capture interop](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createformonitor).
- [Window capture affinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity).
- [DXGI shared resource handle](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgiresource1-createsharedhandle), [keyed mutex ownership](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgikeyedmutex-acquiresync).
- [Composition BeginDraw and atlas offset](https://learn.microsoft.com/en-us/windows/win32/api/windows.ui.composition.interop/nf-windows-ui-composition-interop-icompositiondrawingsurfaceinterop-begindraw).
- [Win32CaptureSample](https://github.com/robmikh/Win32CaptureSample) was consulted for API lifetime/threading; no source was copied. Its [resource-growth report](https://github.com/robmikh/Win32CaptureSample/issues/77) is context, not proof that this machine has the same root cause.
- Actual pixel-change timing and observer limitations are described in [the presentation probe](presentation-probe.md).
