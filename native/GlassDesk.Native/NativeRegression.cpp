// Hidden, in-process ABI/DPI/lifetime regression fixture. It never shows a
// window, injects pointer input, or changes Windows settings. Desktop input and
// compositor appearance are separately verified by the primary GUI workflow.
#include "GlassDeskNative.cpp"
#include <iostream>
#include <thread>
#include <cstring>
#include <cwchar>
#include <dxgi1_4.h>
#include <atomic>

namespace
{
void* testContext{};int callbackCount{},recoveryCount{},enterCount{},moveCount{};bool quitInCallback{};
void __stdcall Callback(void*,GDPointer const* value)
{
    ++callbackCount;
    if(value->kind==1)++enterCount;
    if(value->kind==3)++moveCount;
    if(value->kind==12&&value->controlId==-1)++recoveryCount;
    if(value->kind==14&&quitInCallback)
    {check_hresult(GDDestroy(testContext));testContext=nullptr;}
}
void Require(bool value,char const* message){if(!value)throw std::runtime_error(message);}
void Pump()
{MSG message{};while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessageW(&message);}}
struct PointerCounts {int enters{},moves{},leaves{};};
void __stdcall CountPointers(void* user,GDPointer const* value)
{
    auto& counts=*static_cast<PointerCounts*>(user);
    if(value->kind==1)++counts.enters;
    if(value->kind==2)++counts.leaves;
    if(value->kind==3)++counts.moves;
}
template<typename T> T Export(HMODULE module,char const* name)
{
    auto address=GetProcAddress(module,name);check_bool(address!=nullptr);
    T function{};static_assert(sizeof(function)==sizeof(address));
    std::memcpy(&function,&address,sizeof(function));return function;
}
struct ModuleLifetime
{
    HMODULE module{};
    ~ModuleLifetime(){if(module)FreeLibrary(module);}
};
struct ContextLifetime
{
    void* context{};decltype(&GDDestroy) destroy{};
    ~ContextLifetime(){if(context)destroy(context);}
};
struct TypographyApi
{
    decltype(&GDGetNativeAbiInfo) abi;
    decltype(&GDConfigureTypographyV2) configure;
    decltype(&GDMeasureTextV2) measure;
    decltype(&GDRenderSurfaceV2) render;
    decltype(&GDGetTypographyCacheStats) stats;
    decltype(&GDRenderSurface) renderV1;
};
struct TypographyResult
{GDTypographyCacheStats stats{};float timeDelta{},secondsDelta{};unsigned auditedGlyphRuns{};};
class FontAuditRenderer final : public IDWriteTextRenderer
{
    std::atomic<ULONG> references{1};
public:
    unsigned runs{},simulated{};
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** value)override
    {if(!value)return E_POINTER;*value=nullptr;if(iid==__uuidof(IUnknown)||iid==__uuidof(IDWriteTextRenderer)||iid==__uuidof(IDWritePixelSnapping)){*value=static_cast<IDWriteTextRenderer*>(this);AddRef();return S_OK;}return E_NOINTERFACE;}
    ULONG STDMETHODCALLTYPE AddRef()override{return ++references;}
    ULONG STDMETHODCALLTYPE Release()override{auto value=--references;if(!value)delete this;return value;}
    HRESULT STDMETHODCALLTYPE IsPixelSnappingDisabled(void*,BOOL* value)override{if(!value)return E_POINTER;*value=TRUE;return S_OK;}
    HRESULT STDMETHODCALLTYPE GetCurrentTransform(void*,DWRITE_MATRIX* value)override{if(!value)return E_POINTER;*value={1,0,0,1,0,0};return S_OK;}
    HRESULT STDMETHODCALLTYPE GetPixelsPerDip(void*,FLOAT* value)override{if(!value)return E_POINTER;*value=1;return S_OK;}
    HRESULT STDMETHODCALLTYPE DrawGlyphRun(void*,FLOAT,FLOAT,DWRITE_MEASURING_MODE,DWRITE_GLYPH_RUN const* glyph,DWRITE_GLYPH_RUN_DESCRIPTION const*,IUnknown*)override
    {if(!glyph||!glyph->fontFace)return E_POINTER;++runs;if(glyph->fontFace->GetSimulations()!=DWRITE_FONT_SIMULATIONS_NONE)++simulated;return S_OK;}
    HRESULT STDMETHODCALLTYPE DrawUnderline(void*,FLOAT,FLOAT,DWRITE_UNDERLINE const*,IUnknown*)override{return S_OK;}
    HRESULT STDMETHODCALLTYPE DrawStrikethrough(void*,FLOAT,FLOAT,DWRITE_STRIKETHROUGH const*,IUnknown*)override{return S_OK;}
    HRESULT STDMETHODCALLTYPE DrawInlineObject(void* context,FLOAT x,FLOAT y,IDWriteInlineObject* object,BOOL sideways,BOOL rtl,IUnknown* effect)override
    {return object?object->Draw(context,this,x,y,sideways,rtl,effect):E_POINTER;}
};
TypographyResult CheckTypography(void* context,TypographyApi const& api,gd::Host* internal=nullptr)
{
    Require(sizeof(GDOp)==64&&sizeof(GDOpV2)==96&&offsetof(GDOpV2,fontWeight)==64&&offsetof(GDOpV2,trackingEm)==68
        &&offsetof(GDOpV2,textRole)==72&&offsetof(GDOpV2,lineHeightDip)==76&&offsetof(GDOpV2,baselineDip)==80
        &&offsetof(GDOpV2,textFlags)==84&&offsetof(GDOpV2,localeName)==88&&sizeof(GDNativeAbiInfo)==24
        &&sizeof(GDTextMetrics)==24&&sizeof(GDTypographyCacheStats)==40,"typography ABI sizes and offsets");
    GDNativeAbiInfo abi{sizeof(GDNativeAbiInfo)};check_hresult(api.abi(&abi));
    Require(abi.version==2&&abi.v1Size==64&&abi.v2Size==96&&(abi.capabilities&31)==31&&abi.reserved==0,"V2 ABI negotiation/capabilities including clock faces");
    Require(api.abi(nullptr)==E_POINTER,"ABI info null pointer");GDNativeAbiInfo badAbi{};
    Require(api.abi(&badAbi)==E_INVALIDARG,"ABI info caller size is required");
    Require(api.configure(context,3,1,nullptr)==E_INVALIDARG&&api.configure(context,0,2,nullptr)==E_INVALIDARG,"bad profile/revision rejection");
    Require(api.configure(context,0,1,L"ignored-font.ttf")==E_INVALIDARG,"SystemA must not silently consume a QA font path");
    check_hresult(api.configure(context,0,1,nullptr));
    GDTypographyCacheStats empty{};check_hresult(api.stats(context,&empty));Require(empty.formats==0&&empty.layouts==0,"configuration invalidates typography caches");
    GDOpV2 op{};op.legacy={2,8,4,800,90,0,25,0xff20252c,0,0,L"08:08"};op.fontWeight=400;op.textRole=1;op.lineHeightDip=32;op.textFlags=1;op.localeName=L"en-US";
    GDTextMetrics metrics{};check_hresult(api.measure(context,&op,sizeof(op),&metrics));
    Require(metrics.advance>0&&metrics.widthIncludingTrailingWhitespace>=metrics.advance&&metrics.lineCount==1&&metrics.lineHeight==32,"V2 semantic metrics");
    for(uint32_t face=0;face<=0x300;face+=0x100)
    {
        auto sample=op;sample.textFlags=1|face;float smallest=1000,largest=0;
        for(auto digits:{L"00:00",L"11:11",L"23:59",L"08:08"})
        {
            sample.legacy.text=digits;GDTextMetrics clock{};check_hresult(api.measure(context,&sample,sizeof(sample),&clock));
            Require(clock.advance>0&&clock.advance<96&&clock.height<=32.1f,"clock face fits the real narrow clock slot");
            smallest=std::min(smallest,clock.advance);largest=std::max(largest,clock.advance);
        }
        if(largest-smallest>=.25f)std::cerr<<"clock face "<<face<<" width delta "<<(largest-smallest)<<"\n";
        Require(largest-smallest<.25f,"clock faces retain tabular number width");
    }
    auto wrongFaceRole=op;wrongFaceRole.textRole=13;wrongFaceRole.textFlags=0x101;
    Require(api.measure(context,&wrongFaceRole,sizeof(wrongFaceRole),&metrics)==E_INVALIDARG,"clock face cannot replace body typography");
    GDTypographyCacheStats before{},after{};check_hresult(api.stats(context,&before));
    for(int i=0;i<16;++i)check_hresult(api.measure(context,&op,sizeof(op),&metrics));
    check_hresult(api.stats(context,&after));Require(after.layoutsCreated==before.layoutsCreated&&after.layoutHits>=before.layoutHits+16,"repeated measurement reuses cached layout");
    ::IInspectable* surface{};check_hresult(api.render(context,840,100,&op,1,sizeof(op),&surface));Require(surface!=nullptr,"V2 returned a Composition surface");surface->Release();surface=nullptr;
    GDTypographyCacheStats drawn{};check_hresult(api.stats(context,&drawn));
    Require(drawn.layoutsCreated==after.layoutsCreated&&drawn.layoutHits>after.layoutHits,"V2 measurement/drawing share one layout creator and key");
    auto recolored=op;recolored.legacy.color=0xffeeeeee;recolored.legacy.x=15;check_hresult(api.measure(context,&recolored,sizeof(op),&metrics));
    GDTypographyCacheStats colorStats{};check_hresult(api.stats(context,&colorStats));Require(colorStats.layoutsCreated==drawn.layoutsCreated,"color/position do not rebuild typography layout");
    auto resized=op;resized.legacy.width+=1;check_hresult(api.measure(context,&resized,sizeof(op),&metrics));check_hresult(api.stats(context,&after));Require(after.layoutsCreated==drawn.layoutsCreated+1,"width constraint participates in layout key");
    Require(api.measure(context,&op,64,&metrics)==E_INVALIDARG&&api.render(context,100,100,&op,1,64,&surface)==E_INVALIDARG&&!surface,"wrong V2 stride is rejected");
    auto invalid=op;invalid.fontWeight=-1;Require(api.measure(context,&invalid,sizeof(op),&metrics)==E_INVALIDARG,"negative font weight rejected");
    invalid=op;invalid.trackingEm=std::numeric_limits<float>::quiet_NaN();Require(api.measure(context,&invalid,sizeof(op),&metrics)==E_INVALIDARG,"NaN tracking rejected");
    invalid=op;invalid.lineHeightDip=std::numeric_limits<float>::infinity();Require(api.measure(context,&invalid,sizeof(op),&metrics)==E_INVALIDARG,"infinite line height rejected");
    invalid=op;invalid.baselineDip=-1;Require(api.measure(context,&invalid,sizeof(op),&metrics)==E_INVALIDARG,"negative baseline rejected");
    invalid=op;invalid.textRole=23;Require(api.measure(context,&invalid,sizeof(op),&metrics)==E_INVALIDARG,"unknown text role rejected");
    invalid=op;invalid.textFlags=4;Require(api.measure(context,&invalid,sizeof(op),&metrics)==E_INVALIDARG,"unknown text flags rejected");
    invalid=op;invalid.textRole=0;Require(api.measure(context,&invalid,sizeof(op),&metrics)==E_INVALIDARG,"legacy must not silently discard V2 typography fields");
    HRESULT wrongThread{};std::thread other([&]{GDTextMetrics value{};wrongThread=api.measure(context,&op,sizeof(op),&value);});other.join();Require(wrongThread==RPC_E_WRONG_THREAD,"V2 owner-thread guard");
    TypographyResult result;float minimum=std::numeric_limits<float>::max(),maximum=0;
    for(int hour=0;hour<24;++hour)for(int minute=0;minute<60;++minute)
    {wchar_t time[6]{};swprintf_s(time,L"%02d:%02d",hour,minute);op.legacy.text=time;check_hresult(api.measure(context,&op,sizeof(op),&metrics));minimum=std::min(minimum,metrics.advance);maximum=std::max(maximum,metrics.advance);}
    result.timeDelta=maximum-minimum;Require(result.timeDelta<=.05f,"V2 all 1440 HH:mm combinations must be stable");
    minimum=std::numeric_limits<float>::max();maximum=0;
    for(auto text:{L"00:00:00",L"08:08:08",L"11:11:11",L"12:34:56",L"22:22:22",L"23:59:59"})
    {op.legacy.text=text;check_hresult(api.measure(context,&op,sizeof(op),&metrics));minimum=std::min(minimum,metrics.advance);maximum=std::max(maximum,metrics.advance);}
    result.secondsDelta=maximum-minimum;Require(result.secondsDelta<=.05f,"V2 semantic HH:mm:ss must be stable");
    op.fontWeight=600;op.textRole=9;op.lineHeightDip=28;op.textFlags=0;op.legacy.fontSize=20;
    for(auto text:{L"今天 08:08 · GlassDesk",L"天球(そら)のMúsica",L"العربية / עברית",L"\U0001f469\U0001f3fd\u200d\U0001f4bb / Emoji"})
    {
        op.legacy.text=text;check_hresult(api.measure(context,&op,sizeof(op),&metrics));
        if(internal)
        {
            auto layout=internal->Typography().CreateLayout(internal->TextRequest(op));com_ptr<FontAuditRenderer> audit;audit.attach(new FontAuditRenderer());
            check_hresult(layout->textLayout->Draw(nullptr,audit.get(),0,0));Require(audit->runs>0&&audit->simulated==0,"V2 actual glyph runs must use no simulated fonts");result.auditedGlyphRuns+=audit->runs;
        }
    }
    // V1 consumes the exact original64-byte array and Legacy V2 paints the same
    // branch. No V1 array is interpreted as a96-byte strided array.
    GDOp legacy[3]={{0,0,0,100,40,0,14,0x22112233},{2,4,0,180,40,0,14,0xff20252c,1,0,L"Legacy / 音乐"},{6,185,4,24,24,0,20,0xff20252c,0,1,L"\uE768"}};
    check_hresult(api.renderV1(context,220,48,legacy,3,&surface));surface->Release();surface=nullptr;
    GDOpV2 legacyV2[3]{};for(unsigned i=0;i<3;++i)legacyV2[i].legacy=legacy[i];
    check_hresult(api.render(context,220,48,legacyV2,3,sizeof(GDOpV2),&surface));surface->Release();surface=nullptr;
    check_hresult(api.measure(context,&legacyV2[1],sizeof(GDOpV2),&metrics));Require(metrics.advance>0,"Legacy V2 text can be measured");
    if(internal)
    {
        auto layout=internal->Typography().CreateLayout(internal->TextRequest(legacyV2[1]));
        Require(layout->requestedFamily==L"Segoe UI Variable Text"&&layout->latinWeight==600&&layout->resolvedLocale.empty(),"Legacy helper preserves fixed Text/bool/default locale");
    }
    op.fontWeight=400;op.textRole=13;op.lineHeightDip=20;op.legacy.text=L"bounded cache";
    for(int i=0;i<180;++i){op.legacy.fontSize=12+float(i)/10;check_hresult(api.measure(context,&op,sizeof(op),&metrics));}
    check_hresult(api.stats(context,&result.stats));Require(result.stats.formats<=64&&result.stats.layouts<=128&&result.stats.layoutsCreated>128,"V2 format/layout cache bounds");
    auto saved=result.stats;Require(FAILED(api.configure(context,2,1,nullptr)),"NotoQa requires a real private font path");check_hresult(api.stats(context,&after));Require(after.layoutsCreated==saved.layoutsCreated&&after.layouts==saved.layouts,"failed configuration preserves prior typography state");
    return result;
}
void WriteTypographyResult(TypographyResult const& result)
{
    std::cout<<"{\"abiVersion\":2,\"v1Size\":64,\"v2Size\":96,\"abiInfoSize\":24,\"metricsSize\":24,\"cacheStatsSize\":40,\"hhmmCombinations\":1440,\"hhmmDeltaDip\":"<<result.timeDelta
        <<",\"secondsRepresentativeDeltaDip\":"<<result.secondsDelta<<",\"auditedNonSimulatedGlyphRuns\":"<<result.auditedGlyphRuns<<",\"formats\":"<<result.stats.formats<<",\"layouts\":"<<result.stats.layouts<<",\"layoutsCreated\":"<<result.stats.layoutsCreated<<",\"measureDrawSharedCache\":true,\"v1LegacyCompatible\":true}";
}
int LoadedDllTypography(wchar_t const* path)
{
    ModuleLifetime module{LoadLibraryExW(path,nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)};check_bool(module.module!=nullptr);
    auto create=Export<decltype(&GDCreate)>(module.module,"GDCreate");auto destroy=Export<decltype(&GDDestroy)>(module.module,"GDDestroy");
    TypographyApi api{Export<decltype(&GDGetNativeAbiInfo)>(module.module,"GDGetNativeAbiInfo"),Export<decltype(&GDConfigureTypographyV2)>(module.module,"GDConfigureTypographyV2"),Export<decltype(&GDMeasureTextV2)>(module.module,"GDMeasureTextV2"),Export<decltype(&GDRenderSurfaceV2)>(module.module,"GDRenderSurfaceV2"),Export<decltype(&GDGetTypographyCacheStats)>(module.module,"GDGetTypographyCacheStats"),Export<decltype(&GDRenderSurface)>(module.module,"GDRenderSurface")};
    auto foreground=GetForegroundWindow();ContextLifetime context{nullptr,destroy};HWND window{};check_hresult(create(nullptr,nullptr,&context.context,&window));
    auto result=CheckTypography(context.context,api);Require(!IsWindowVisible(window)&&GetForegroundWindow()==foreground,"loaded-DLL typography test stays hidden and preserves foreground");
    std::cout<<"{\"passed\":true,\"testKind\":\"loaded DLL hidden typography ABI V2\",\"neverVisible\":true,\"systemSettingsChanged\":false,\"typography\":";WriteTypographyResult(result);std::cout<<"}\n";return 0;
}
int HiddenLiquid(wchar_t const* path)
{
    // Real monitor capture and real GPU post-processing, without showing either
    // host HWND or injecting desktop input. Dynamic-background presentation is
    // still a separate GUI acceptance check.
    HWND window{};void* context{};check_hresult(GDCreate(nullptr,nullptr,&context,&window));
    ContextLifetime lifetime{context,GDDestroy};auto host=gd::FromContext(context);
    DWORD bodyBefore{},shadowBefore{};check_bool(GetWindowDisplayAffinity(window,&bodyBefore));check_bool(GetWindowDisplayAffinity(host->shadow,&shadowBefore));
    RECT work{};SystemParametersInfoW(SPI_GETWORKAREA,0,&work,0);check_hresult(GDSetPlacement(context,work.left+80,work.top+80));
    host->visible=true; // logical capture policy; the actual HWNDs stay hidden
    check_hresult(GDSetLiquidGlassEnabled(context,1));
    auto deadline=GetTickCount64()+7000;
    while(GetTickCount64()<deadline&&!host->liquidReady){Pump();host->UpdateLiquid();Sleep(34);}
    std::wstring status=host->liquid?host->liquid->Status():L"No renderer";
    if(!host->liquidReady)
    {
        check_hresult(GDSetLiquidGlassEnabled(context,0));
        DWORD bodyRestored{},shadowRestored{};check_bool(GetWindowDisplayAffinity(window,&bodyRestored));check_bool(GetWindowDisplayAffinity(host->shadow,&shadowRestored));
        Require(bodyBefore==bodyRestored&&shadowBefore==shadowRestored,"failed WGC capture must restore exact affinities");
        std::cout<<"{\"passed\":false,\"testKind\":\"never-visible real WGC GPU prototype\",\"captureAffinityRestoredOnFailure\":true,\"bodyBefore\":"<<bodyBefore<<",\"bodyAfter\":"<<bodyRestored<<",\"shadowBefore\":"<<shadowBefore<<",\"shadowAfter\":"<<shadowRestored<<",\"presentedVisualVerified\":false}\n";
        std::wcerr<<L"WGC/GPU prototype did not become ready: "<<status<<L"\n";return 2;
    }
    DWORD bodyActive{},shadowActive{};check_bool(GetWindowDisplayAffinity(window,&bodyActive));check_bool(GetWindowDisplayAffinity(host->shadow,&shadowActive));
    Require(bodyActive==WDA_EXCLUDEFROMCAPTURE&&shadowActive==WDA_EXCLUDEFROMCAPTURE,"both host and shadow capture exclusion readback");
    Require(!IsWindowVisible(window)&&!IsWindowVisible(host->shadow),"capture prototype must never show HWNDs");
    auto sampleUntil=GetTickCount64()+2000;
    while(GetTickCount64()<sampleUntil){Pump();host->UpdateLiquid();Sleep(34);}
    auto stats=host->liquid->Stats();
    check_hresult(GDSaveLiquidGlassDiagnosticFrame(context,path));
    check_hresult(GDSetVisible(context,0));
    Require(host->liquidEnabled&&!host->liquid->Ready()&&!host->liquid->Surface(),"hidden capture must release surface and retain enablement intent");
    check_hresult(GDSetLiquidGlassEnabled(context,0));
    DWORD bodyAfter{},shadowAfter{};check_bool(GetWindowDisplayAffinity(window,&bodyAfter));check_bool(GetWindowDisplayAffinity(host->shadow,&shadowAfter));
    Require(bodyBefore==bodyAfter&&shadowBefore==shadowAfter,"liquid disable must restore exact original capture affinity");
    std::cout<<"{\"passed\":true,\"testKind\":\"never-visible real WGC GPU prototype\",\"bothWindowsExcluded\":true,\"bodyBefore\":"<<bodyBefore<<",\"bodyActive\":"<<bodyActive<<",\"bodyAfter\":"<<bodyAfter
        <<",\"shadowBefore\":"<<shadowBefore<<",\"shadowActive\":"<<shadowActive<<",\"shadowAfter\":"<<shadowAfter<<",\"pauseReleasedSurface\":true,\"captureAffinityRestored\":true,\"presentedVisualVerified\":false,\"frames\":"<<stats.frames
        <<",\"captureWidth\":"<<stats.captureWidth<<",\"captureHeight\":"<<stats.captureHeight<<",\"panelWidth\":"<<stats.panelWidth<<",\"panelHeight\":"<<stats.panelHeight
        <<",\"estimatedTextureBytes\":"<<stats.estimatedTextureBytes<<",\"gpuSamples\":"<<stats.gpuSamples<<",\"gpuFilterAverageMilliseconds\":"<<stats.gpuAverageMilliseconds<<",\"gpuFilterMaximumMilliseconds\":"<<stats.gpuMaximumMilliseconds
        <<",\"gpuPipelineSpanAverageMilliseconds\":"<<stats.gpuPipelineAverageMilliseconds<<",\"gpuPipelineSpanMaximumMilliseconds\":"<<stats.gpuPipelineMaximumMilliseconds
        <<",\"captureCallbacks\":"<<stats.captureCallbacks<<",\"receivedCaptureFrames\":"<<stats.receivedCaptureFrames<<"}\n";
    return 0;
}
int ActiveLiquidCycles(unsigned cycles)
{
    Require(cycles>0&&cycles<=1000,"cycle count must be 1..1000");
    HWND window{};void* context{};check_hresult(GDCreate(nullptr,nullptr,&context,&window));
    ContextLifetime lifetime{context,GDDestroy};auto host=gd::FromContext(context);
    com_ptr<IDXGIAdapter> adapter;check_hresult(host->d3d.as<IDXGIDevice>()->GetAdapter(adapter.put()));
    auto memory=adapter.as<IDXGIAdapter3>();
    auto usage=[&]() {
        DXGI_QUERY_VIDEO_MEMORY_INFO local{},shared{};
        check_hresult(memory->QueryVideoMemoryInfo(0,DXGI_MEMORY_SEGMENT_GROUP_LOCAL,&local));
        check_hresult(memory->QueryVideoMemoryInfo(0,DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL,&shared));
        return local.CurrentUsage+shared.CurrentUsage;
    };
    auto before=usage();host->visible=true;
    for(unsigned index=1;index<=cycles;++index)
    {
        check_hresult(GDSetLiquidGlassEnabled(context,1));
        auto deadline=GetTickCount64()+7000;
        while(GetTickCount64()<deadline&&!host->liquidReady){Pump();host->UpdateLiquid();Sleep(16);}
        Require(host->liquidReady,"active WGC cycle must receive a real frame");
        auto worker=OpenProcess(SYNCHRONIZE,FALSE,host->liquid->Stats().workerPid);check_bool(worker!=nullptr);
        check_hresult(GDSetLiquidGlassEnabled(context,0));
        auto exited=WaitForSingleObject(worker,1000)==WAIT_OBJECT_0;CloseHandle(worker);
        Require(exited,"disabling must terminate the owned capture worker");
        DWORD body{},shadow{};check_bool(GetWindowDisplayAffinity(window,&body));check_bool(GetWindowDisplayAffinity(host->shadow,&shadow));
        Require(body==0&&shadow==0&&!host->liquid->Surface(),"disable releases material and restores capture affinity");
        auto until=GetTickCount64()+80;while(GetTickCount64()<until){Pump();Sleep(10);}
        auto current=usage();
        std::cout<<"{\"cycle\":"<<index<<",\"gpuUsageBytes\":"<<current<<",\"baselineGpuBytes\":"<<before<<",\"captureWorkerExited\":true}"<<std::endl;
        Require(current<=before+128ull*1024*1024,"active capture cycles must not accumulate unbounded GPU memory");
    }
    Require(!IsWindowVisible(window)&&!IsWindowVisible(host->shadow),"native lifecycle fixture must never show a window");
    std::cout<<"{\"passed\":true,\"testKind\":\"never-visible active WGC lifecycle\",\"cycles\":"<<cycles<<",\"baselineGpuBytes\":"<<before<<",\"finalGpuBytes\":"<<usage()<<"}"<<std::endl;
    return 0;
}
int CaptureOwnerExit()
{
    HWND window{};void* context{};check_hresult(GDCreate(nullptr,nullptr,&context,&window));
    auto host=gd::FromContext(context);host->visible=true;
    check_hresult(GDSetLiquidGlassEnabled(context,1));
    auto deadline=GetTickCount64()+7000;
    while(GetTickCount64()<deadline&&!host->liquidReady){Pump();host->UpdateLiquid();Sleep(16);}
    Require(host->liquidReady,"ownership fixture requires a live WGC worker");
    std::cout<<"{\"ownerPid\":"<<GetCurrentProcessId()<<",\"workerPid\":"<<host->liquid->Stats().workerPid<<",\"abruptOwnerExit\":true}"<<std::endl;
    ExitProcess(0); // Deliberately bypass destructors to test the kernel job boundary.
}
int LoadedDllTracking(wchar_t const* path)
{
    // This mode calls the supplied binary's exported ABI, rather than the
    // implementation compiled into this fixture. It never reads its Host.
    ModuleLifetime module{LoadLibraryExW(path,nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)};
    check_bool(module.module!=nullptr);
    auto create=Export<decltype(&GDCreate)>(module.module,"GDCreate");
    auto destroy=Export<decltype(&GDDestroy)>(module.module,"GDDestroy");
    auto hide=Export<decltype(&GDSetVisible)>(module.module,"GDSetVisible");
    auto getContext=Export<decltype(&GDGetContext)>(module.module,"GDGetContext");
    auto foreground=GetForegroundWindow();PointerCounts counts{};
    ContextLifetime context{nullptr,destroy};HWND window{};
    check_hresult(create(CountPointers,&counts,&context.context,&window));
    Require(!IsWindowVisible(window),"DLL fixture must start hidden");
    GDContext geometry{};check_hresult(getContext(context.context,&geometry));
    auto center=MAKELPARAM(int(std::lround(geometry.width*geometry.dpi/192.0f)),int(std::lround(geometry.height*geometry.dpi/192.0f)));
    check_hresult(hide(context.context,0));
    // A hidden HWND can still receive a mouse message queued before hiding.
    // Check both synchronous dispatch and delivery through the message queue.
    SendMessageW(window,WM_MOUSEMOVE,0,center);
    int sentEnters=counts.enters,sentMoves=counts.moves;
    check_hresult(hide(context.context,0));
    int beforeEnters=counts.enters,beforeMoves=counts.moves;
    check_bool(PostMessageW(window,WM_MOUSEMOVE,0,center));Pump();
    int postedEnters=counts.enters-beforeEnters,postedMoves=counts.moves-beforeMoves;
    bool foregroundPreserved=GetForegroundWindow()==foreground;
    bool neverVisible=!IsWindowVisible(window);
    bool passed=sentEnters==0&&sentMoves==0&&postedEnters==0&&postedMoves==0&&foregroundPreserved&&neverVisible;
    check_hresult(destroy(context.context));context.context=nullptr;
    Require(!IsWindow(window),"DLL fixture must destroy the hidden HWND");
    std::cout<<"{\"passed\":"<<(passed?"true":"false")<<",\"testKind\":\"hidden mouse messages against loaded DLL\",\"systemSettingsChanged\":false,\"hiddenTrackingCases\":2,\"hiddenSendEnter\":"<<sentEnters<<",\"hiddenSendMove\":"<<sentMoves<<",\"hiddenPostEnter\":"<<postedEnters<<",\"hiddenPostMove\":"<<postedMoves<<",\"foregroundPreserved\":"<<(foregroundPreserved?"true":"false")<<",\"neverVisible\":"<<(neverVisible?"true":"false")<<"}\n";
    return passed?0:1;
}
std::vector<unsigned char> Bitmap(unsigned color)
{
    constexpr int width=1024,height=16;
    std::vector<unsigned char> bytes(sizeof(BITMAPFILEHEADER)+sizeof(BITMAPINFOHEADER)+width*height*4);
    BITMAPFILEHEADER file{};file.bfType=0x4D42;file.bfSize=static_cast<DWORD>(bytes.size());file.bfOffBits=sizeof(file)+sizeof(BITMAPINFOHEADER);
    BITMAPINFOHEADER info{};info.biSize=sizeof(info);info.biWidth=width;info.biHeight=height;info.biPlanes=1;info.biBitCount=32;info.biCompression=BI_RGB;
    std::memcpy(bytes.data(),&file,sizeof(file));std::memcpy(bytes.data()+sizeof(file),&info,sizeof(info));
    for(size_t index=file.bfOffBits;index<bytes.size();index+=4)std::memcpy(bytes.data()+index,&color,4);
    return bytes;
}
}
int wmain(int argc,wchar_t** argv)
{
    try
    {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        init_apartment(apartment_type::single_threaded);
        Require(!gd::CaptureWaitExpired(99,100),"pre-start timestamp must not underflow into a false capture timeout");
        Require(!gd::CaptureWaitExpired(5099,100)&&gd::CaptureWaitExpired(5100,100),"capture timeout must respect the five-second boundary");
        if(argc==3&&std::wcscmp(argv[1],L"--tracking-dll")==0)return LoadedDllTracking(argv[2]);
        if(argc==3&&std::wcscmp(argv[1],L"--typography-dll")==0)return LoadedDllTypography(argv[2]);
        if(argc==3&&std::wcscmp(argv[1],L"--liquid-hidden")==0)return HiddenLiquid(argv[2]);
        if(argc==3&&std::wcscmp(argv[1],L"--liquid-cycles")==0)return ActiveLiquidCycles(static_cast<unsigned>(std::wcstoul(argv[2],nullptr,10)));
        if(argc==2&&std::wcscmp(argv[1],L"--capture-owner-exit")==0)return CaptureOwnerExit();
        Require(argc==1,"usage: GlassDesk.NativeRegression.exe [--tracking-dll absolute-DLL-path | --typography-dll absolute-DLL-path | --liquid-hidden absolute-PNG-path | --liquid-cycles count]");
        HWND window{};check_hresult(GDCreate(Callback,nullptr,&testContext,&window));
        auto host=gd::FromContext(testContext);
        Require(!IsWindowVisible(window)&&!IsWindowVisible(host->shadow),"fixture must remain hidden");
        auto shadow=host->shadow;
        auto foreground=GetForegroundWindow();
        GDFrame frame{272.5f,178.25f,23.125f,20,.62f,0};check_hresult(GDApplyFrame(testContext,&frame));
        for(unsigned dpiValue:{96u,120u,144u,192u,96u,144u})
        {
            RECT suggestion{40,60,500,400};SendMessageW(window,WM_DPICHANGED,MAKELONG(dpiValue,dpiValue),reinterpret_cast<LPARAM>(&suggestion));
            GDContext context{};check_hresult(GDGetContext(testContext,&context));RECT rectangle{};GetClientRect(window,&rectangle);
            Require(context.width==frame.width&&context.height==frame.height&&context.dpi==dpiValue,"DPI must preserve current animated DIP frame");
            Require(rectangle.right==int(std::lround(frame.width*dpiValue/96))&&rectangle.bottom==int(std::lround(frame.height*dpiValue/96)),"canonical DIP to pixel conversion");
        }
        SendMessageW(window,WM_SETTINGCHANGE,0,0);
        Require(host->dpi==host->CurrentMonitorDpi(),"setting notification must repair stale DPI");
        // Exercise visible-model input while the actual HWND remains hidden.
        // This reproduces an armed tracking state without showing a window.
        auto hiddenCenter=MAKELPARAM(int(std::lround(frame.width*host->Scale()/2)),int(std::lround(frame.height*host->Scale()/2)));
        host->visible=true;int beforeArmEnters=enterCount;
        SendMessageW(window,WM_MOUSEMOVE,0,hiddenCenter);
        Require(host->tracking&&enterCount==beforeArmEnters+1,"visible model must arm mouse tracking");
        Require(!IsWindowVisible(window)&&!IsWindowVisible(shadow),"visible model must not show either HWND");
        check_hresult(GDSetVisible(testContext,0));
        bool trackingClearedOnHide=!host->tracking;
        int beforeHiddenEnters=enterCount,beforeHiddenMoves=moveCount;
        check_bool(PostMessageW(window,WM_MOUSEMOVE,0,hiddenCenter));Pump();
        int hiddenEnters=enterCount-beforeHiddenEnters,hiddenMoves=moveCount-beforeHiddenMoves;
        bool hiddenMessageSuppressed=hiddenEnters==0&&hiddenMoves==0&&!host->tracking;
        // Restore only the logical model, preserving the never-visible rule.
        // The first inside move must emit a fresh Enter for the Core hover timer.
        host->visible=true;int beforeRestoreEnters=enterCount;
        SendMessageW(window,WM_MOUSEMOVE,0,hiddenCenter);
        int restoredEnter=enterCount-beforeRestoreEnters;
        bool freshEnterAfterRestore=restoredEnter==1&&host->tracking;
        check_hresult(GDSetVisible(testContext,0));
        bool hiddenTrackingPassed=trackingClearedOnHide&&hiddenMessageSuppressed&&freshEnterAfterRestore;
        Require(!IsWindowVisible(window)&&!IsWindowVisible(shadow),"mouse regression must remain hidden");
        HRESULT wrongThread=S_OK;
        std::thread worker([&]{GDContext context{};wrongThread=GDGetContext(testContext,&context);});worker.join();
        Require(wrongThread==RPC_E_WRONG_THREAD,"owner thread guard");
        ::IInspectable* compositor{};::IInspectable* content{};
        check_hresult(GDGetComposition(testContext,&compositor,&content));compositor->Release();content->Release();
        GDOp operations[8]{};
        operations[0]={0,2,2,100,20,0,14,0x44222222};
        operations[1]={1,4,28,110,24,8,14,0x88606060};
        operations[2]={2,8,2,150,48,0,14,0xff202632,1,1,L"音乐 / Unicode / DPI"};
        operations[3]={3,160,4,64,64,12,14,0xffffffff};
        operations[4]={4,90,90,-30,-15,1.5f,14,0xff202632};
        operations[5]={5,12,90,18,18,0,14,0xff559977};
        operations[6]={6,190,88,24,24,0,24,0xff202632,0,1,L"\uE768"};
        operations[7]={7,34,80,148,40,0,12,0xff202632,0,0,L"Two lines of status text / 换行"};
        for(unsigned color:{0xff0044ffu,0xff88cc22u,0xffaa44bbu,0xff4499aau})
        {
            auto bytes=Bitmap(color);operations[3].image=bytes.data();operations[3].imageSize=static_cast<unsigned>(bytes.size());
            ::IInspectable* result{};check_hresult(GDRenderSurface(testContext,240,130,operations,8,&result));result->Release();
            Require(!host->images.empty()&&host->images.size()<=3,"decoded bitmap cache is bounded");
            auto size=host->images.front().bitmap->GetPixelSize();Require(std::max(size.width,size.height)<=768,"artwork decode limit");
        }
        Require(host->images.size()==3,"recent-three cache eviction");
        host->CreateGraphics();host->CreateGrain();
        operations[3].image=nullptr;operations[3].imageSize=0;
        ::IInspectable* result{};check_hresult(GDRenderSurface(testContext,240,130,operations,8,&result));result->Release();
        PostMessageW(window,gd::RecoveredMessage,0,0);Pump();Require(recoveryCount==1,"queued graphics redraw signal");
        TypographyApi typographyApi{GDGetNativeAbiInfo,GDConfigureTypographyV2,GDMeasureTextV2,GDRenderSurfaceV2,GDGetTypographyCacheStats,GDRenderSurface};
        auto typographyResult=CheckTypography(testContext,typographyApi,host.get());
        // Repeated geometry changes must release the previous HBITMAP/DC. These
        // are hidden CPU/ABI checks, not a claim about presented 60 FPS.
        auto gdiBefore=GetGuiResources(GetCurrentProcess(),GR_GDIOBJECTS);
        auto userBefore=GetGuiResources(GetCurrentProcess(),GR_USEROBJECTS);
        wchar_t liquidStatus[512]{};
        for(int index=0;index<100;++index)
        {
            check_hresult(GDSetLiquidGlassEnabled(testContext,1));check_hresult(GDGetLiquidGlassStatus(testContext,liquidStatus,512));
            Require(host->liquidEnabled&&std::wcsstr(liquidStatus,L"hidden"),"hidden enabled liquid must preserve user intent and report pause");
            Require(!host->liquid->Surface()&&!host->liquid->Ready(),"hidden liquid must allocate no capture/material surface");
            check_hresult(GDSetLiquidGlassEnabled(testContext,0));
        }
        for(int index=0;index<1000;++index)
        {
            auto height=index%3==0?48.0f:(index%3==1?132.0f:464.0f);
            auto width=index%3==0?232.0f:360.0f;
            GDFrame value{width,height,24,20,.62f,0};check_hresult(GDApplyFrame(testContext,&value));
            if(index%20==0)Pump();
        }
        auto gdiAfter=GetGuiResources(GetCurrentProcess(),GR_GDIOBJECTS);
        auto userAfter=GetGuiResources(GetCurrentProcess(),GR_USEROBJECTS);
        HRGN region=CreateRectRgn(0,0,0,0);Require(GetWindowRgn(window,region)!=ERROR,"rounded HWND region exists");
        RECT finalClient{};GetClientRect(window,&finalClient);
        Require(!PtInRegion(region,0,0)&&PtInRegion(region,finalClient.right/2,0),"AA margin preserves top pixels and excludes corner input");DeleteObject(region);
        Require(gdiAfter<=gdiBefore+1&&userAfter<=userBefore+1,"1000 geometry changes must not leak GDI/USER handles");
        auto provider=host->provider;
        quitInCallback=true;SendMessageW(window,WM_CLOSE,0,0);
        Require(testContext==nullptr&&!IsWindow(window)&&!IsWindow(shadow),"reentrant Quit callback must dispose both HWNDs");
        VARIANT property{};Require(provider->GetPropertyValue(UIA_NamePropertyId,&property)==UIA_E_ELEMENTNOTAVAILABLE,"retained UIA provider must be safely disconnected");
        Require(GetForegroundWindow()==foreground,"hidden fixture must preserve foreground");
        std::cout<<"{\"passed\":"<<(hiddenTrackingPassed?"true":"false")<<",\"testKind\":\"hidden ABI/message regression\",\"systemSettingsChanged\":false,\"dpiCases\":6,\"geometryChanges\":1000,\"hiddenTrackingCases\":3,\"trackingClearedOnHide\":"<<(trackingClearedOnHide?"true":"false")<<",\"hiddenMessageSuppressed\":"<<(hiddenMessageSuppressed?"true":"false")<<",\"freshEnterAfterRestore\":"<<(freshEnterAfterRestore?"true":"false")<<",\"hiddenEnter\":"<<hiddenEnters<<",\"hiddenMove\":"<<hiddenMoves<<",\"restoredEnter\":"<<restoredEnter<<",\"gdiBefore\":"<<gdiBefore<<",\"gdiAfter\":"<<gdiAfter<<",\"userBefore\":"<<userBefore<<",\"userAfter\":"<<userAfter<<",\"callbacks\":"<<callbackCount<<",\"typography\":";
        WriteTypographyResult(typographyResult);std::cout<<"}\n";
        return hiddenTrackingPassed?0:1;
    }
    catch(hresult_error const& error)
    {std::cerr<<"native-regression HRESULT 0x"<<std::hex<<static_cast<unsigned>(error.code())<<"\n";return 1;}
    catch(std::exception const& error){std::cerr<<error.what()<<"\n";return 1;}
}
