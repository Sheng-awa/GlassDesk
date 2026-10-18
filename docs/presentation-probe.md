# QA capture and presentation probes

Build with `scripts/build-capture-diagnostics.ps1`. These executables are QA tools; neither is loaded by the application or installed as a runtime dependency. They never show a window or send desktop input.

`artifacts/capture-diagnostics/Release/GlassDesk.CaptureDiagnostic.exe` is a small official-equivalent WGC receive diagnostic: default D3D11 BGRA device, monitor item, free-threaded frame pool, callback that immediately retrieves/closes the frame. It reports desktop/session identity, monitor/item dimensions, arrival/frame counts, and HRESULTs. Options `--no-cursor`, `--exclude`, `--warp`, and `--mta` isolate receiving conditions. `--exclude` creates two physically hidden test HWNDs, excludes them from capture, and destroys them after the diagnostic; no visible window is shown. No pixels are saved. This tool proved that the host's early timeout was an implementation defect rather than an unavailable desktop capture environment.

Run the actual presentation observer before application animation stress:

```
GlassDesk.PresentationProbe.exe --pid <actual-GlassDesk-PID> --seconds 30 --output <absolute-JSON-path>
```

The observer requires the actual native widget to be visible with liquid glass disabled and capture affinity `WDA_NONE`. It selects the monitor's matching DXGI adapter/output and reads Desktop Duplication `LastPresentTime` (an OS QPC timestamp) and `AccumulatedFrames`. Cursor-only updates are excluded. A temporary ROI texture is copied/mapped for a 64-bit pixel hash, then discarded; only hashes, timestamp metadata, and HWND rectangles are persisted. No video or frame pixels are saved, and the production renderer performs no CPU readback.

The ROI grows to the union of observed widget rectangles, then stays fixed. Changing crop size can alter a hash without a newly presented widget image, so every ROI growth resets interval tracking and is excluded from cadence statistics. This also means the first expansion is not counted as FPS proof. Later changes inside the stable ROI supply observed pixel-change timestamps. The report's resize/move interval distribution uses consecutive actual pixel changes accompanying changing HWND geometry within 100 ms, isolating active motion from dwell periods. It includes P50/P95/max, intervals over 25/33.4 ms, desktop-frame misses, and protected-frame counts.

Keep the desktop underneath and around the ROI static and uncovered. Changes from another window, wallpaper animation, or other pixels inside the ROI can confound attribution. A static probe run should contain no motion samples; a duration or nominal timer rate does not establish 60 FPS. Missing/accumulated frames reveal observer limitations and must be reported alongside the measured intervals. `ProtectedContentMaskedOut` is a desktop-wide flag and does not identify which window was masked. Rotation or moving beyond the selected monitor is explicitly unsupported by this version of the probe.

The probe is an observation of delivered desktop images containing the widget, not a measurement of physical panel scanout. Its ROI evidence is stronger than compositor-wide cadence, but production scene/input testing is still needed to attribute those changes and assess animation continuity.

Implementation smoke check on the actual visible application (PID 10272) succeeded for one second: 213 desktop-image acquisitions, zero motion intervals for the static widget, one accumulated/missed desktop frame, and zero protected frames. This confirms target matching, cross-process capture-affinity reading, desktop duplication, hash generation, and metadata output. It is deliberately not treated as animation-FPS acceptance; the real stress run is separate. Metadata is in `artifacts/native-material-check/presentation-probe-smoke.json`.

Primary API sources: [Microsoft Desktop Duplication](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api), [frame timestamp/accumulation semantics](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/ns-dxgi1_2-dxgi_outdupl_frame_info), and [AcquireNextFrame](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutputduplication-acquirenextframe).
