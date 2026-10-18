#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include "LiquidGlass.h"
#include "CaptureChannel.h"
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <windows.ui.composition.interop.h>
#include <dwmapi.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <winrt/Windows.Foundation.Metadata.h>
#include <d3dcompiler.h>
#include <d3d11_1.h>
#include <d3d11_4.h>
#include <dxgi.h>
#include <wincodec.h>
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstring>
#include <cstdio>
#include <utility>
#include <mutex>

using namespace winrt;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
using namespace winrt::Windows::UI::Composition;

namespace gd
{
namespace
{
// Original material. A small prefilter avoids sparse-kernel banding on text.
// The frosted center and clearer curved bevel
// sample the same real desktop frame; application content is never distorted.
// Shape and coordinates are physical pixels; content is a separate visual.
constexpr char shader[]=R"HLSL(
cbuffer Parameters : register(b0) {
    float2 textureSize; float2 panelSize;
    float2 panelOrigin; float radius; float blurRadius;
    float2 axis; float2 padding;
};
Texture2D source : register(t0);
Texture2D unfiltered : register(t1);
SamplerState linearClamp : register(s0);
struct Varying { float4 position : SV_POSITION; float2 uv : TEXCOORD; };
Varying VS(uint id : SV_VertexID) {
    Varying o; o.uv=float2((id<<1)&2,id&2);
    o.position=float4(o.uv*float2(2,-2)+float2(-1,1),0,1); return o;
}
float4 Blur(float2 uv,float2 direction) {
    if(blurRadius<0.01) return float4(source.SampleLevel(linearClamp,uv,0).rgb,1);
    float sigma=max(blurRadius*0.48,0.01);
    float stride=blurRadius/6.0;
    float3 color=0; float weightSum=0;
    [unroll] for(int i=-6;i<=6;i++) {
        float weight=exp(-0.5*pow(i*stride/sigma,2));
        color+=source.SampleLevel(linearClamp,uv+direction*(i*stride)/textureSize,0).rgb*weight;
        weightSum+=weight;
    }
    return float4(color/weightSum,1);
}
float4 Horizontal(Varying i) : SV_TARGET { return Blur(i.uv,float2(1,0)); }
float4 Prefilter(Varying i) : SV_TARGET {
    if(blurRadius<4) return source.SampleLevel(linearClamp,i.uv,0);
    float2 d=(blurRadius>=12?1.0:0.5)/textureSize;
    return (source.SampleLevel(linearClamp,i.uv+float2(-d.x,-d.y),0)
        +source.SampleLevel(linearClamp,i.uv+float2(d.x,-d.y),0)
        +source.SampleLevel(linearClamp,i.uv+float2(-d.x,d.y),0)
        +source.SampleLevel(linearClamp,i.uv+d,0))*0.25;
}
float4 Glass(Varying i) : SV_TARGET {
    float2 p=i.uv*panelSize;
    float2 halfPanel=panelSize*0.5;
    float roundness=clamp(radius,0.0,min(halfPanel.x,halfPanel.y));
    float2 local=p-halfPanel;
    float2 q=abs(local)-(halfPanel-roundness);
    float2 outside=max(q,0);
    float outsideLength=length(outside);
    float distance=outsideLength+min(max(q.x,q.y),0)-roundness;
    // The rounded distance gradient meets the straight edges continuously.
    // Guard its length rather than normalizing an empty corner vector.
    float2 normal=outsideLength>0.0001?outside/max(outsideLength,0.0001)*sign(local)
        :q.x>q.y?float2(sign(local.x),0):float2(0,sign(local.y));
    float inside=max(0,-distance);
    float bevelWidth=min(clamp(roundness*0.70,12.0,32.0),min(halfPanel.x,halfPanel.y));
    float t=saturate(inside/max(bevelWidth,0.001));
    // This finite C2 profile has no slope/curvature seam at the frosted
    // interior. The same normal and profile also run around every corner.
    float bevel=1-t*t*t*(t*(t*6-15)+10);
    float bend=bevel*bevel*clamp(roundness*0.48,12.0,28.0);
    float2 uv=(panelOrigin+p-normal*bend)/textureSize;
    float3 frost=Blur(uv,float2(0,1)).rgb;
    float3 clear=unfiltered.SampleLevel(linearClamp,uv,0).rgb;
    float3 lumaWeights=float3(0.2126,0.7152,0.0722);
    // Keep an edge lens while containing sharp desktop text and busy images.
    // Both reads belong to this frame; this adds no history or capture work.
    float detail=smoothstep(0.06,0.28,abs(dot(clear-frost,lumaWeights)));
    float3 color=lerp(frost,clear,0.18+bevel*lerp(0.70,0.55,detail));
    // Neutral interior lift preserves the clear dark foreground on a dark
    // desktop. The bevel stays more transparent and distinct from the center.
    float luminance=dot(color,float3(0.2126,0.7152,0.0722));
    float veil=saturate((0.22-luminance)/max(1-luminance,0.01))*(1-bevel*0.92);
    color=lerp(color,float3(1,1,1),veil);
    float light=dot(normal,normalize(float2(-0.55,-1.0)));
    float rim=exp(-inside/0.95);
    float shoulder=4*bevel*(1-bevel);
    // A shallow absorption shoulder and lower contact rim imply thickness.
    // Headroom-aware light keeps white backgrounds from becoming a hot frame.
    color*=1-0.016*shoulder-0.065*rim*max(-light,0);
    float highlight=rim*(0.055+0.16*max(light,0))+0.018*bevel*max(light,0);
    color=lerp(color,float3(1,1,1),highlight);
    return float4(saturate(color),1);
}
)HLSL";
struct Parameters {float textureSize[2],panelSize[2],panelOrigin[2],radius,blur,axis[2],padding[2];};
static_assert(sizeof(Parameters)==48);
struct Texture
{
    com_ptr<ID3D11Texture2D> value;
    com_ptr<ID3D11ShaderResourceView> source;
    com_ptr<ID3D11RenderTargetView> target;
    unsigned width{},height{};
    void Reset(){value=nullptr;source=nullptr;target=nullptr;width=height=0;}
};
struct CropReadLease
{
    ID3D11DeviceContext* context{};IDXGIKeyedMutex* mutex{};
    ~CropReadLease(){context->Flush();mutex->ReleaseSync(0);}
};
HRESULT LastError(){auto error=GetLastError();return HRESULT_FROM_WIN32(error?error:ERROR_GEN_FAILURE);}

// This lease never touches D3D, Composition, or CaptureChannel from its watchdog.
// It is joined and destroyed while both HWNDs are still owned by the host.
struct SnapshotAffinityLease
{
    HWND body{},shadow{};
    DWORD bodyBefore{},shadowBefore{},bodyReadback{},shadowReadback{};
    std::mutex mutex;
    PTP_TIMER watchdog{};
    ULONGLONG deadline{};
    HRESULT restoreError{E_PENDING};
    bool restored{};

    SnapshotAffinityLease(HWND bodyValue,HWND shadowValue,DWORD bodyAffinity,DWORD shadowAffinity)
        :body(bodyValue),shadow(shadowValue),bodyBefore(bodyAffinity),shadowBefore(shadowAffinity),
         bodyReadback(bodyAffinity),shadowReadback(shadowAffinity)
    {
        watchdog=CreateThreadpoolTimer([](PTP_CALLBACK_INSTANCE,void* value,PTP_TIMER)noexcept
        {static_cast<SnapshotAffinityLease*>(value)->Restore();},this,nullptr);
        check_bool(watchdog!=nullptr);
    }
    ~SnapshotAffinityLease(){CloseWatchdog();Restore();}
    void CloseWatchdog()noexcept
    {
        if(!watchdog)return;
        SetThreadpoolTimer(watchdog,nullptr,0,0);
        WaitForThreadpoolTimerCallbacks(watchdog,TRUE);
        CloseThreadpoolTimer(watchdog);watchdog=nullptr;
    }
    static HRESULT SetAndRead(HWND window,DWORD expected,DWORD& actual)noexcept
    {
        if(!IsWindow(window))return HRESULT_FROM_WIN32(ERROR_INVALID_WINDOW_HANDLE);
        if(!SetWindowDisplayAffinity(window,expected))return LastError();
        if(!GetWindowDisplayAffinity(window,&actual))return LastError();
        return actual==expected?S_OK:E_ACCESSDENIED;
    }
    void Expose()
    {
        check_hresult(SetAndRead(body,WDA_NONE,bodyReadback));
        check_hresult(SetAndRead(shadow,WDA_NONE,shadowReadback));
    }
    void Arm(unsigned milliseconds)noexcept
    {
        deadline=GetTickCount64()+milliseconds;
        FILETIME now{};GetSystemTimeAsFileTime(&now);
        ULARGE_INTEGER due{};due.LowPart=now.dwLowDateTime;due.HighPart=now.dwHighDateTime;
        // Absolute time also expires across sleep; the watchdog restores on wake.
        due.QuadPart+=uint64_t(milliseconds)*10000;
        FILETIME when{due.LowPart,due.HighPart};SetThreadpoolTimer(watchdog,&when,0,0);
    }
    HRESULT Restore()noexcept
    {
        std::lock_guard lock(mutex);
        if(restored)return S_OK;
        auto bodyError=SetAndRead(body,bodyBefore,bodyReadback);
        auto shadowError=SetAndRead(shadow,shadowBefore,shadowReadback);
        restoreError=FAILED(bodyError)?bodyError:shadowError;
        restored=SUCCEEDED(restoreError);return restoreError;
    }
    bool Restored()noexcept{std::lock_guard lock(mutex);return restored;}
    void Describe(wchar_t* text,size_t capacity,wchar_t const* phase)noexcept
    {
        std::lock_guard lock(mutex);
        swprintf_s(text,capacity,L"QA final-UI snapshot: %s; affinity %s; body=%lu expected=%lu; shadow=%lu expected=%lu; restore=0x%08X",
            phase,restored?L"restored and read back":L"not yet restored",bodyReadback,bodyBefore,shadowReadback,shadowBefore,static_cast<unsigned>(restoreError));
    }
};
}

struct LiquidGlass::Impl
{
    HWND body{},shadow{};
    com_ptr<ID3D11Device> device;com_ptr<ID3D11DeviceContext> context;
    CaptureChannel capture;
    CompositionGraphicsDevice graphics{nullptr};
    HMONITOR monitor{};RECT monitorBounds{};
    winrt::Windows::Graphics::SizeInt32 captureSize{};
    com_ptr<ID3D11VertexShader> vertex;com_ptr<ID3D11PixelShader> prefilterShader,horizontal,glass;
    com_ptr<ID3D11SamplerState> sampler;com_ptr<ID3D11Buffer> parameters;
    com_ptr<ID3D11Query> gpuStart,gpuMaterialEnd,gpuEnd,gpuDisjoint;bool gpuPending{};
    uint64_t gpuSamples{};double gpuTotal{},gpuMaximum{},gpuPipelineTotal{},gpuPipelineMaximum{};
    Texture crop,prefilter,scratch,output;
    Texture incoming;
    com_ptr<IDXGIKeyedMutex> cropRead;
    int panelCropX{},panelCropY{};
    CompositionDrawingSurface surface{nullptr};
    bool enabled{},hidden{true},solid{},ready{},affinitySaved{},waitingCaptureFrame{};
    DWORD bodyAffinity{},shadowAffinity{};
    ULONGLONG lastTick{},retryAfter{},waitingSince{};uint64_t frames{};
    std::wstring reason{L"Disabled"};
    wchar_t const* operation{L"monitor capture or GPU material"};
    std::unique_ptr<SnapshotAffinityLease> snapshot;
    RECT snapshotPanel{};
    wchar_t snapshotSummary[384]{};

    Impl(HWND bodyValue,HWND shadowValue,ID3D11Device* deviceValue,ID3D11DeviceContext* contextValue,CompositionGraphicsDevice const& graphicsValue)
        :body(bodyValue),shadow(shadowValue),graphics(graphicsValue)
    {device.copy_from(deviceValue);context.copy_from(contextValue);}

    ~Impl(){Stop();RestoreAffinity();}
    void RestoreAffinity()noexcept
    {
        if(!affinitySaved)return;
        bool restored=true;
        if(IsWindow(body)&&!SetWindowDisplayAffinity(body,bodyAffinity))restored=false;
        if(IsWindow(shadow)&&!SetWindowDisplayAffinity(shadow,shadowAffinity))restored=false;
        if(restored)affinitySaved=false;
    }
    void Exclude()
    {
        operation=L"read original body/shadow capture affinity";
        if(!affinitySaved)
        {
            check_bool(GetWindowDisplayAffinity(body,&bodyAffinity));
            check_bool(GetWindowDisplayAffinity(shadow,&shadowAffinity));
            affinitySaved=true;
        }
        operation=L"exclude body from capture";
        if(!SetWindowDisplayAffinity(body,WDA_EXCLUDEFROMCAPTURE))throw hresult_error(LastError());
        operation=L"exclude shadow from capture";
        if(!SetWindowDisplayAffinity(shadow,WDA_EXCLUDEFROMCAPTURE))throw hresult_error(LastError());
        operation=L"verify body/shadow capture exclusion";
        DWORD bodyValue{},shadowValue{};
        check_bool(GetWindowDisplayAffinity(body,&bodyValue));check_bool(GetWindowDisplayAffinity(shadow,&shadowValue));
        if(bodyValue!=WDA_EXCLUDEFROMCAPTURE||shadowValue!=WDA_EXCLUDEFROMCAPTURE)throw hresult_error(E_ACCESSDENIED,L"Window capture exclusion did not persist");
    }
    void Stop()noexcept
    {
        // Cancel/join the independent watchdog before any host HWND can die.
        // Lifecycle cleanup then restores the ordinary pre-capture affinities.
        if(snapshot)
        {
            snapshot->CloseWatchdog();snapshot->Restore();
            snapshot->Describe(snapshotSummary,std::size(snapshotSummary),L"cancelled by renderer lifecycle; worker stopped");
            snapshot.reset();
        }
        ready=false;waitingCaptureFrame=false;capture.Stop();monitor=nullptr;
        cropRead=nullptr;incoming.Reset();crop.Reset();prefilter.Reset();scratch.Reset();output.Reset();
        try{if(surface)surface.Close();}catch(...){}surface=nullptr;
        vertex=nullptr;prefilterShader=nullptr;horizontal=nullptr;glass=nullptr;sampler=nullptr;parameters=nullptr;
        gpuStart=nullptr;gpuMaterialEnd=nullptr;gpuEnd=nullptr;gpuDisjoint=nullptr;gpuPending=false;
        if(context){context->ClearState();context->Flush();}
    }
    void Fail(HRESULT error,wchar_t const* failedOperation)noexcept
    {
        Stop();RestoreAffinity();retryAfter=GetTickCount64()+5000;
        wchar_t text[256]{};swprintf_s(text,L"Fallback: %s (0x%08X); enabled intent retained; retry in 5s",failedOperation,static_cast<unsigned>(error));
        reason=text;
    }
    void CreateShaders()
    {
        auto compile=[](char const* entry,char const* profile)
        {
            com_ptr<ID3DBlob> code,error;
            check_hresult(D3DCompile(shader,sizeof(shader)-1,"GlassDesk.LiquidGlass",nullptr,nullptr,entry,profile,D3DCOMPILE_OPTIMIZATION_LEVEL3,0,code.put(),error.put()));
            return code;
        };
        auto vs=compile("VS","vs_4_0"),ps=compile("Prefilter","ps_4_0"),hs=compile("Horizontal","ps_4_0"),gs=compile("Glass","ps_4_0");
        check_hresult(device->CreateVertexShader(vs->GetBufferPointer(),vs->GetBufferSize(),nullptr,vertex.put()));
        check_hresult(device->CreatePixelShader(ps->GetBufferPointer(),ps->GetBufferSize(),nullptr,prefilterShader.put()));
        check_hresult(device->CreatePixelShader(hs->GetBufferPointer(),hs->GetBufferSize(),nullptr,horizontal.put()));
        check_hresult(device->CreatePixelShader(gs->GetBufferPointer(),gs->GetBufferSize(),nullptr,glass.put()));
        D3D11_SAMPLER_DESC sampling{};sampling.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        sampling.AddressU=sampling.AddressV=sampling.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;sampling.MaxLOD=D3D11_FLOAT32_MAX;
        check_hresult(device->CreateSamplerState(&sampling,sampler.put()));
        D3D11_BUFFER_DESC constants{};constants.ByteWidth=sizeof(Parameters);constants.Usage=D3D11_USAGE_DEFAULT;constants.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
        check_hresult(device->CreateBuffer(&constants,nullptr,parameters.put()));
        D3D11_QUERY_DESC query{D3D11_QUERY_TIMESTAMP,0};
        check_hresult(device->CreateQuery(&query,gpuStart.put()));check_hresult(device->CreateQuery(&query,gpuMaterialEnd.put()));check_hresult(device->CreateQuery(&query,gpuEnd.put()));
        query.Query=D3D11_QUERY_TIMESTAMP_DISJOINT;check_hresult(device->CreateQuery(&query,gpuDisjoint.put()));
    }
    void Start(HMONITOR value)
    {
        Stop();Exclude();
        if(!device||!context||!graphics)throw hresult_error(E_FAIL,L"D3D rendering device is unavailable");
        operation=L"compile HLSL/create D3D11 material shaders";CreateShaders();
        MONITORINFO info{sizeof(info)};check_bool(GetMonitorInfoW(value,&info));monitorBounds=info.rcMonitor;monitor=value;
        captureSize={monitorBounds.right-monitorBounds.left,monitorBounds.bottom-monitorBounds.top};
        StartCaptureWorker();
    }
    void StartCaptureWorker()
    {
        // QA restart deliberately gets a fresh shared texture: no WGC frame or
        // keyed-mutex state from the unexcluded screenshot interval can survive.
        cropRead=nullptr;incoming.Reset();
        D3D11_TEXTURE2D_DESC desc{};desc.Width=captureSize.Width;desc.Height=captureSize.Height;
        desc.MipLevels=desc.ArraySize=1;desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;
        desc.Usage=D3D11_USAGE_DEFAULT;desc.BindFlags=D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_RENDER_TARGET;
        desc.MiscFlags=D3D11_RESOURCE_MISC_SHARED_NTHANDLE|D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;
        check_hresult(device->CreateTexture2D(&desc,nullptr,incoming.value.put()));
        incoming.width=desc.Width;incoming.height=desc.Height;cropRead=incoming.value.as<IDXGIKeyedMutex>();
        operation=L"start job-owned WGC capture worker";
        capture.Start(device.get(),incoming.value.get(),monitor,desc.Width,desc.Height);
        waitingSince=GetTickCount64();waitingCaptureFrame=true;reason=L"Starting: waiting for first monitor frame";
    }
    HRESULT FinishSnapshot()noexcept
    {
        if(!snapshot)return S_FALSE;
        snapshot->CloseWatchdog();
        auto restored=snapshot->Restore();
        snapshot->Describe(snapshotSummary,std::size(snapshotSummary),FAILED(restored)?L"restore failed; material frozen and worker stopped":L"restored; worker pending UI-thread restart");
        if(FAILED(restored))return restored; // Never sample a possibly unexcluded HWND.
        snapshot.reset();
        try
        {
            operation=L"synchronize restored QA snapshot capture exclusion";
            check_hresult(DwmFlush());
            operation=L"resume job-owned capture after QA final-UI snapshot";
            if(enabled&&!hidden&&!solid)
            {
                StartCaptureWorker();
                wcscpy_s(snapshotSummary,L"QA final-UI snapshot: restored and read back body=17 shadow=17; worker restarted on UI thread with fresh shared texture; enablement unchanged");
            }
            return S_OK;
        }
        catch(...)
        {
            auto error=to_hresult();Fail(error,operation);
            wcscpy_s(snapshotSummary,L"QA final-UI snapshot: affinity restored and read back; capture restart failed; ordinary fallback active; enablement retained");
            return error;
        }
    }
    void Ensure(Texture& texture,unsigned width,unsigned height)
    {
        if(texture.value&&texture.width==width&&texture.height==height)return;
        texture.Reset();D3D11_TEXTURE2D_DESC desc{};desc.Width=width;desc.Height=height;desc.MipLevels=desc.ArraySize=1;
        desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.Usage=D3D11_USAGE_DEFAULT;
        desc.BindFlags=D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_RENDER_TARGET;
        check_hresult(device->CreateTexture2D(&desc,nullptr,texture.value.put()));
        check_hresult(device->CreateShaderResourceView(texture.value.get(),nullptr,texture.source.put()));
        check_hresult(device->CreateRenderTargetView(texture.value.get(),nullptr,texture.target.put()));texture.width=width;texture.height=height;
    }
    void Pass(Texture const& source,Texture const& target,ID3D11PixelShader* pixel,Parameters const& constants,ID3D11ShaderResourceView* raw=nullptr)
    {
        context->UpdateSubresource(parameters.get(),0,nullptr,&constants,0,0);
        ID3D11ShaderResourceView* input[]{source.source.get(),raw};auto render=target.target.get();auto buffer=parameters.get();auto sample=sampler.get();
        context->OMSetRenderTargets(1,&render,nullptr);context->PSSetShaderResources(0,2,input);
        context->PSSetConstantBuffers(0,1,&buffer);context->PSSetSamplers(0,1,&sample);
        D3D11_VIEWPORT viewport{0,0,float(target.width),float(target.height),0,1};context->RSSetViewports(1,&viewport);
        context->IASetInputLayout(nullptr);context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->VSSetShader(vertex.get(),nullptr,0);context->PSSetShader(pixel,nullptr,0);
        context->OMSetBlendState(nullptr,nullptr,0xffffffff);context->OMSetDepthStencilState(nullptr,0);context->RSSetState(nullptr);
        context->Draw(3,0);input[0]=input[1]=nullptr;render=nullptr;
        context->PSSetShaderResources(0,2,input);context->OMSetRenderTargets(1,&render,nullptr);
    }
    void Present()
    {
        if(!surface||surface.Size().Width!=float(output.width)||surface.Size().Height!=float(output.height))
        {
            if(surface)surface.Close();
            surface=graphics.CreateDrawingSurface({float(output.width),float(output.height)},DirectXPixelFormat::B8G8R8A8UIntNormalized,DirectXAlphaMode::Premultiplied);
        }
        auto interop=surface.as<ABI::Windows::UI::Composition::ICompositionDrawingSurfaceInterop>();
        com_ptr<IDXGISurface> destination;POINT offset{};
        check_hresult(interop->BeginDraw(nullptr,__uuidof(IDXGISurface),destination.put_void(),&offset));
        try
        {
            auto texture=destination.as<ID3D11Texture2D>();
            context->CopySubresourceRegion(texture.get(),0,static_cast<UINT>(offset.x),static_cast<UINT>(offset.y),0,output.value.get(),0,nullptr);
        }
        catch(...){interop->EndDraw();throw;}
        check_hresult(interop->EndDraw());
    }
    void Tick(RECT panel,float radius,float blur)
    {
        if(!enabled||hidden||solid)return;
        if(snapshot)
        {
            // Retain the exact already-presented material. Geometry changes end
            // the QA window rather than stretching a diagnostic into new evidence.
            if(!snapshot->Restored()&&GetTickCount64()<snapshot->deadline&&EqualRect(&panel,&snapshotPanel))return;
            if(FAILED(FinishSnapshot()))return;
        }
        auto now=GetTickCount64();if(now<retryAfter||now-lastTick<34)return;lastTick=now;
        auto current=MonitorFromRect(&panel,MONITOR_DEFAULTTONEAREST);
        if(!capture.process||monitor!=current){Start(current);now=GetTickCount64();}
        operation=L"receive WGC monitor frame";
        check_hresult(device->GetDeviceRemovedReason());
        auto childError=InterlockedCompareExchange(&capture.control->error,0,0);
        if(childError!=E_PENDING)check_hresult(childError);
        if(WaitForSingleObject(capture.process,0)==WAIT_OBJECT_0)throw hresult_error(E_FAIL,L"Capture worker exited");
        if(gpuPending)
        {
            D3D11_QUERY_DATA_TIMESTAMP_DISJOINT timing{};UINT64 first{},materialEnd{},last{};
            if(context->GetData(gpuDisjoint.get(),&timing,sizeof(timing),D3D11_ASYNC_GETDATA_DONOTFLUSH)==S_OK&&
                context->GetData(gpuStart.get(),&first,sizeof(first),D3D11_ASYNC_GETDATA_DONOTFLUSH)==S_OK&&
                context->GetData(gpuMaterialEnd.get(),&materialEnd,sizeof(materialEnd),D3D11_ASYNC_GETDATA_DONOTFLUSH)==S_OK&&
                context->GetData(gpuEnd.get(),&last,sizeof(last),D3D11_ASYNC_GETDATA_DONOTFLUSH)==S_OK)
            {
                gpuPending=false;
                if(!timing.Disjoint&&timing.Frequency&&last>=first)
                {
                    double material=1000.0*double(materialEnd-first)/double(timing.Frequency),pipeline=1000.0*double(last-first)/double(timing.Frequency);
                    ++gpuSamples;gpuTotal+=material;gpuMaximum=std::max(gpuMaximum,material);gpuPipelineTotal+=pipeline;gpuPipelineMaximum=std::max(gpuPipelineMaximum,pipeline);
                }
            }
        }
        auto size=captureSize;
        auto width=panel.right-panel.left,height=panel.bottom-panel.top;
        if(width<=0||height<=0)return;
        int padding=int(std::ceil(std::min(blur,100.0f)+17));
        int left=std::clamp(int(panel.left-monitorBounds.left-padding),0,size.Width-1);
        int top=std::clamp(int(panel.top-monitorBounds.top-padding),0,size.Height-1);
        int right=std::clamp(int(panel.right-monitorBounds.left+padding),left+1,size.Width);
        int bottom=std::clamp(int(panel.bottom-monitorBounds.top+padding),top+1,size.Height);
        operation=L"allocate GPU material textures";
        capture.Request({left,top,right,bottom});
        auto locked=cropRead->AcquireSync(1,0);
        if(locked==WAIT_TIMEOUT)
        {
            if(waitingCaptureFrame&&CaptureWaitExpired(now,waitingSince))throw hresult_error(HRESULT_FROM_WIN32(WAIT_TIMEOUT),L"Monitor capture returned no frames");
            return;
        }
        if(locked!=S_OK)throw hresult_error(FAILED(locked)?locked:E_FAIL);
        {
            CropReadLease lease{context.get(),cropRead.get()};
            if(InterlockedCompareExchange(&capture.control->frameRequest,0,0)!=InterlockedCompareExchange(&capture.control->request,0,0))return;
            Ensure(crop,static_cast<unsigned>(right-left),static_cast<unsigned>(bottom-top));
            D3D11_BOX copied{0,0,0,crop.width,crop.height,1};
            context->CopySubresourceRegion(crop.value.get(),0,0,0,0,incoming.value.get(),0,&copied);
        }
        unsigned divisor=blur>=12?4u:blur>=4?2u:1u;
        unsigned filteredWidth=(crop.width+divisor-1)/divisor,filteredHeight=(crop.height+divisor-1)/divisor;
        Ensure(prefilter,filteredWidth,filteredHeight);Ensure(scratch,filteredWidth,filteredHeight);
        Ensure(output,static_cast<unsigned>(width),static_cast<unsigned>(height));
        bool measure=!gpuPending;if(measure){context->Begin(gpuDisjoint.get());context->End(gpuStart.get());}
        Parameters constants{{float(crop.width),float(crop.height)},{float(width),float(height)},
            {float(panel.left-monitorBounds.left-left),float(panel.top-monitorBounds.top-top)},radius,std::clamp(blur,0.0f,100.0f),{}, {}};
        panelCropX=panel.left-monitorBounds.left-left;panelCropY=panel.top-monitorBounds.top-top;
        operation=L"HLSL blur/refraction and Composition copy";
        try
        {
            Pass(crop,prefilter,prefilterShader.get(),constants);
            Pass(prefilter,scratch,horizontal.get(),constants);
            Pass(scratch,output,glass.get(),constants,crop.source.get());
            if(measure)context->End(gpuMaterialEnd.get());Present();
        }
        catch(...)
        {
            if(measure){context->End(gpuEnd.get());context->End(gpuDisjoint.get());}
            throw;
        }
        if(measure){context->End(gpuEnd.get());context->End(gpuDisjoint.get());gpuPending=true;}
        check_hresult(device->GetDeviceRemovedReason());ready=true;waitingCaptureFrame=false;++frames;
        wchar_t gpu[128]{};
        if(gpuSamples)swprintf_s(gpu,L"; GPU filter avg=%.3fms max=%.3fms; pipeline span avg=%.3fms (%llu samples)",gpuTotal/double(gpuSamples),gpuMaximum,gpuPipelineTotal/double(gpuSamples),static_cast<unsigned long long>(gpuSamples));
        reason=L"Running: WGC monitor → GPU crop → HLSL blur/refraction → Composition; <=29.4 Hz; frames="+std::to_wstring(frames)+L"; worker="+std::to_wstring(capture.Pid())+gpu;
    }
};

LiquidGlass::LiquidGlass(HWND body,HWND shadow,ID3D11Device* device,ID3D11DeviceContext* context,CompositionGraphicsDevice const& graphics)
    :impl(std::make_unique<Impl>(body,shadow,device,context,graphics)){}
LiquidGlass::~LiquidGlass()=default;
void LiquidGlass::SetEnabled(bool value)noexcept
{
    if(impl->enabled==value)return;impl->enabled=value;impl->retryAfter=0;
    if(!value){impl->Stop();impl->RestoreAffinity();impl->reason=impl->affinitySaved?L"Disabled; capture affinity restoration failed":L"Disabled";}
    else impl->reason=impl->hidden?L"Paused: host hidden; enabled intent retained":L"Starting";
}
void LiquidGlass::Suspend(bool hidden,bool solid)noexcept
{
    if(hidden==impl->hidden&&solid==impl->solid)return;
    impl->hidden=hidden;impl->solid=solid;impl->retryAfter=0;
    if(hidden||solid){impl->Stop();impl->RestoreAffinity();}
    if(impl->enabled)impl->reason=hidden?L"Paused: host hidden; enabled intent retained":solid?L"Fallback: solid/high-contrast policy; enabled intent retained":L"Starting";
}
void LiquidGlass::Tick(RECT panel,float radius,float blur)noexcept
{try{impl->Tick(panel,radius,blur);}catch(...){impl->Fail(to_hresult(),impl->operation);}}
bool LiquidGlass::Enabled()const noexcept{return impl->enabled;}
bool LiquidGlass::Ready()const noexcept{return impl->ready;}
std::wstring LiquidGlass::Status()const{return impl->reason;}
LiquidGlassStats LiquidGlass::Stats()const noexcept
{
    uint64_t textureBytes=(uint64_t(impl->crop.width)*impl->crop.height+uint64_t(impl->prefilter.width)*impl->prefilter.height+uint64_t(impl->scratch.width)*impl->scratch.height+
        uint64_t(impl->output.width)*impl->output.height+uint64_t(impl->output.width)*impl->output.height)*4;
    if(impl->capture.process)textureBytes+=uint64_t(impl->captureSize.Width)*impl->captureSize.Height*12;
    uint64_t callbacks{},received{};
    if(impl->capture.control){callbacks=InterlockedCompareExchange(&impl->capture.control->callbacks,0,0);received=InterlockedCompareExchange(&impl->capture.control->received,0,0);}
    return {impl->frames,textureBytes,impl->gpuSamples,static_cast<unsigned>(impl->captureSize.Width),static_cast<unsigned>(impl->captureSize.Height),
        impl->output.width,impl->output.height,impl->gpuSamples?impl->gpuTotal/double(impl->gpuSamples):0,impl->gpuMaximum,
        impl->gpuSamples?impl->gpuPipelineTotal/double(impl->gpuSamples):0,impl->gpuPipelineMaximum,callbacks,received,impl->capture.Pid()};
}
CompositionDrawingSurface LiquidGlass::Surface()const{return impl->surface;}
HRESULT LiquidGlass::BeginDiagnosticSnapshot(unsigned durationMilliseconds)noexcept
{
    if(durationMilliseconds<500||durationMilliseconds>5000)return E_INVALIDARG;
    if(impl->snapshot)return HRESULT_FROM_WIN32(ERROR_BUSY);
    if(!impl->enabled||impl->hidden||impl->solid||!impl->ready||impl->waitingCaptureFrame||!impl->surface||!impl->output.value||!impl->capture.process)
        return HRESULT_FROM_WIN32(ERROR_NOT_READY);
    try
    {
        impl->operation=L"prepare QA final-UI snapshot of a ready material frame";
        check_hresult(impl->device->GetDeviceRemovedReason());
        if(WaitForSingleObject(impl->capture.process,0)!=WAIT_TIMEOUT)throw hresult_error(E_FAIL,L"Capture worker is not running");
        check_bool(GetWindowRect(impl->body,&impl->snapshotPanel));
        if(impl->snapshotPanel.right-impl->snapshotPanel.left!=static_cast<int>(impl->output.width)||
            impl->snapshotPanel.bottom-impl->snapshotPanel.top!=static_cast<int>(impl->output.height))
            return HRESULT_FROM_WIN32(ERROR_NOT_READY);
        DWORD body{},shadow{};
        check_bool(GetWindowDisplayAffinity(impl->body,&body));check_bool(GetWindowDisplayAffinity(impl->shadow,&shadow));
        if(body!=WDA_EXCLUDEFROMCAPTURE||shadow!=WDA_EXCLUDEFROMCAPTURE)
            throw hresult_error(E_ACCESSDENIED,L"QA snapshot requires verified body/shadow capture exclusion");
        impl->snapshot=std::make_unique<SnapshotAffinityLease>(impl->body,impl->shadow,body,shadow);
        // Finish ownership cleanup before making either HWND capturable. The
        // processed output and Composition surface remain alive and untouched.
        handle stoppedWorker;
        check_bool(DuplicateHandle(GetCurrentProcess(),impl->capture.process,GetCurrentProcess(),stoppedWorker.put(),SYNCHRONIZE,FALSE,0));
        impl->capture.Stop();
        auto exited=WaitForSingleObject(stoppedWorker.get(),0);
        if(exited!=WAIT_OBJECT_0)throw hresult_error(exited==WAIT_FAILED?LastError():HRESULT_FROM_WIN32(WAIT_TIMEOUT),L"QA snapshot worker shutdown was not confirmed");
        impl->cropRead=nullptr;impl->incoming.Reset();
        impl->operation=L"temporarily expose body/shadow for a QA final-UI screenshot";
        impl->snapshot->Expose();impl->snapshot->Arm(durationMilliseconds);
        check_hresult(DwmFlush());
        if(impl->snapshot->Restored()||GetTickCount64()>=impl->snapshot->deadline)
        {
            impl->FinishSnapshot();return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
        }
        impl->reason=L"QA final-UI snapshot: material frozen; worker stopped; external screenshot window open; automatic affinity restore armed";
        return S_OK;
    }
    catch(...)
    {
        auto error=to_hresult();
        // A partial exposure is rolled back to the exact pre-QA affinities and
        // normal sampling when possible. Failed restoration remains frozen;
        // failed restart follows the ordinary material fallback path.
        if(impl->snapshot)impl->FinishSnapshot();else impl->Fail(error,impl->operation);
        return error;
    }
}
HRESULT LiquidGlass::EndDiagnosticSnapshot()noexcept{return impl->FinishSnapshot();}
std::wstring LiquidGlass::DiagnosticSnapshotStatus()const
{
    if(impl->snapshot)
    {
        wchar_t status[384]{};
        impl->snapshot->Describe(status,std::size(status),impl->snapshot->Restored()?
            L"watchdog restored affinity; material frozen until UI resumes":L"active: material frozen; worker stopped; external screenshot window open");
        return status;
    }
    return *impl->snapshotSummary?impl->snapshotSummary:L"QA final-UI snapshot: inactive; never requested";
}
HRESULT LiquidGlass::SaveDiagnostic(wchar_t const* path)noexcept
{
    if(!path||!*path)return E_INVALIDARG;
    if(!impl->ready||!impl->output.value)return HRESULT_FROM_WIN32(ERROR_NOT_READY);
    try
    {
        auto saveTexture=[&](ID3D11Texture2D* texture,wchar_t const* target)
        {
        D3D11_TEXTURE2D_DESC desc{};texture->GetDesc(&desc);desc.Usage=D3D11_USAGE_STAGING;
        desc.BindFlags=desc.MiscFlags=0;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        com_ptr<ID3D11Texture2D> staging;check_hresult(impl->device->CreateTexture2D(&desc,nullptr,staging.put()));
        impl->context->CopyResource(staging.get(),texture);D3D11_MAPPED_SUBRESOURCE pixels{};
        check_hresult(impl->context->Map(staging.get(),0,D3D11_MAP_READ,0,&pixels));
        try
        {
            com_ptr<IWICImagingFactory> factory;check_hresult(CoCreateInstance(CLSID_WICImagingFactory2,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(factory.put())));
            com_ptr<IWICStream> stream;check_hresult(factory->CreateStream(stream.put()));check_hresult(stream->InitializeFromFilename(target,GENERIC_WRITE));
            com_ptr<IWICBitmapEncoder> encoder;check_hresult(factory->CreateEncoder(GUID_ContainerFormatPng,nullptr,encoder.put()));check_hresult(encoder->Initialize(stream.get(),WICBitmapEncoderNoCache));
            com_ptr<IWICBitmapFrameEncode> frame;check_hresult(encoder->CreateNewFrame(frame.put(),nullptr));check_hresult(frame->Initialize(nullptr));check_hresult(frame->SetSize(desc.Width,desc.Height));
            WICPixelFormatGUID format=GUID_WICPixelFormat32bppBGRA;check_hresult(frame->SetPixelFormat(&format));
            check_hresult(frame->WritePixels(desc.Height,pixels.RowPitch,pixels.RowPitch*desc.Height,static_cast<BYTE*>(pixels.pData)));
            check_hresult(frame->Commit());check_hresult(encoder->Commit());
        }
        catch(...){impl->context->Unmap(staging.get(),0);throw;}
        impl->context->Unmap(staging.get(),0);
        };
        saveTexture(impl->output.value.get(),path);
        if(impl->panelCropX>=0&&impl->panelCropY>=0&&unsigned(impl->panelCropX)+impl->output.width<=impl->crop.width&&unsigned(impl->panelCropY)+impl->output.height<=impl->crop.height)
        {
            D3D11_TEXTURE2D_DESC desc{};impl->output.value->GetDesc(&desc);desc.BindFlags=0;
            com_ptr<ID3D11Texture2D> reference;check_hresult(impl->device->CreateTexture2D(&desc,nullptr,reference.put()));
            D3D11_BOX box{static_cast<UINT>(impl->panelCropX),static_cast<UINT>(impl->panelCropY),0,static_cast<UINT>(impl->panelCropX)+desc.Width,static_cast<UINT>(impl->panelCropY)+desc.Height,1};
            impl->context->CopySubresourceRegion(reference.get(),0,0,0,0,impl->crop.value.get(),0,&box);
            auto backgroundPath=std::wstring(path)+L".background.png";saveTexture(reference.get(),backgroundPath.c_str());
        }
        return S_OK;
    }
    catch(...){return to_hresult();}
}
}
