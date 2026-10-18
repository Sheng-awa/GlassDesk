#pragma once
#include <windows.h>
#include <d3d11.h>
#include <winrt/Windows.UI.Composition.h>
#include <memory>
#include <string>

namespace gd
{
// Guard a timestamp taken before startup against unsigned subtraction wrap.
inline bool CaptureWaitExpired(uint64_t now,uint64_t started,uint64_t timeout=5000)noexcept
{return now>=started&&now-started>=timeout;}
struct LiquidGlassStats
{
    uint64_t frames{},estimatedTextureBytes{},gpuSamples{};
    unsigned captureWidth{},captureHeight{},panelWidth{},panelHeight{};
    double gpuAverageMilliseconds{},gpuMaximumMilliseconds{},gpuPipelineAverageMilliseconds{},gpuPipelineMaximumMilliseconds{};
    uint64_t captureCallbacks{},receivedCaptureFrames{};
    unsigned workerPid{};
};
// Public methods run on the host UI thread. WGC's free-threaded arrival only
// hands off one latest frame; <=30Hz GPU/Composition work stays on the UI thread.
class LiquidGlass
{
public:
    LiquidGlass(HWND body,HWND shadow,ID3D11Device* device,ID3D11DeviceContext* context,
        winrt::Windows::UI::Composition::CompositionGraphicsDevice const& graphics);
    ~LiquidGlass();
    LiquidGlass(LiquidGlass const&)=delete;
    LiquidGlass& operator=(LiquidGlass const&)=delete;
    void SetEnabled(bool enabled) noexcept;
    void Suspend(bool hidden,bool solid) noexcept;
    void Tick(RECT panel,float radiusPixels,float blurPixels) noexcept;
    bool Enabled()const noexcept;
    bool Ready()const noexcept;
    std::wstring Status()const;
    LiquidGlassStats Stats()const noexcept;
    winrt::Windows::UI::Composition::CompositionDrawingSurface Surface()const;
    HRESULT SaveDiagnostic(wchar_t const* path) noexcept;
    // Explicit QA only: retains the current material while the worker is stopped,
    // temporarily permits an external final-UI screenshot, then restores affinity.
    HRESULT BeginDiagnosticSnapshot(unsigned durationMilliseconds) noexcept;
    HRESULT EndDiagnosticSnapshot() noexcept;
    std::wstring DiagnosticSnapshotStatus()const;
private:
    struct Impl;
    std::unique_ptr<Impl> impl;
};
}
