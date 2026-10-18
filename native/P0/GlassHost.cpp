#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <windowsx.h>
#include <dwmapi.h>
#include <DispatcherQueue.h>
#include <windows.ui.composition.interop.h>
#include <windows.graphics.effects.interop.h>
#include <d2d1effects.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Graphics.Effects.h>
#include <winrt/Windows.System.h>
#include <winrt/Windows.UI.Composition.Desktop.h>
#include <fstream>
#include <filesystem>
#include <string>
#include <cmath>

using namespace winrt;
using namespace Windows::Foundation;
using namespace Windows::Graphics::Effects;
using namespace Windows::UI::Composition;
using namespace Windows::UI::Composition::Desktop;
using EffectInterop = ABI::Windows::Graphics::Effects::IGraphicsEffectD2D1Interop;

// Original small effect descriptor; Windows supplies the GPU implementation.
struct BlurEffect : implements<BlurEffect, IGraphicsEffect, IGraphicsEffectSource, EffectInterop>
{
    hstring name{L"GlassBlur"};
    IGraphicsEffectSource source{nullptr};
    hstring Name() const { return name; }
    void Name(hstring const& value) { name = value; }
    HRESULT __stdcall GetEffectId(GUID* id) noexcept override { *id = CLSID_D2D1GaussianBlur; return S_OK; }
    HRESULT __stdcall GetNamedPropertyMapping(LPCWSTR value, UINT* index,
        ABI::Windows::Graphics::Effects::GRAPHICS_EFFECT_PROPERTY_MAPPING* mapping) noexcept override
    {
        if (wcscmp(value, L"BlurAmount") != 0) return E_INVALIDARG;
        *index = D2D1_GAUSSIANBLUR_PROP_STANDARD_DEVIATION;
        *mapping = ABI::Windows::Graphics::Effects::GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT;
        return S_OK;
    }
    HRESULT __stdcall GetPropertyCount(UINT* count) noexcept override { *count = 3; return S_OK; }
    HRESULT __stdcall GetProperty(UINT index, ABI::Windows::Foundation::IPropertyValue** value) noexcept override
    {
        try
        {
            IPropertyValue property{nullptr};
            if (index == 0) property = PropertyValue::CreateSingle(20.0f).as<IPropertyValue>();
            else if (index == 1) property = PropertyValue::CreateUInt32(D2D1_GAUSSIANBLUR_OPTIMIZATION_BALANCED).as<IPropertyValue>();
            else if (index == 2) property = PropertyValue::CreateUInt32(D2D1_BORDER_MODE_HARD).as<IPropertyValue>();
            else return E_INVALIDARG;
            *value = static_cast<ABI::Windows::Foundation::IPropertyValue*>(detach_abi(property));
            return S_OK;
        } catch (...) { return to_hresult(); }
    }
    HRESULT __stdcall GetSource(UINT index, ABI::Windows::Graphics::Effects::IGraphicsEffectSource** value) noexcept override
    {
        if (index != 0) return E_INVALIDARG;
        auto copy = source;
        *value = static_cast<ABI::Windows::Graphics::Effects::IGraphicsEffectSource*>(detach_abi(copy));
        return S_OK;
    }
    HRESULT __stdcall GetSourceCount(UINT* count) noexcept override { *count = 1; return S_OK; }
};

struct Host
{
    HWND window{};
    Compositor compositor{nullptr};
    DesktopWindowTarget target{nullptr};
    ContainerVisual root{nullptr};
    SpriteVisual glass{nullptr};
    SpriteVisual tint{nullptr};
    CompositionEffectBrush backdrop{nullptr};
    UINT dpi{96};
    static constexpr int widthDip = 360, heightDip = 132;
    bool receiver{};
    int clicks{};
    POINT press{}, origin{};
    bool pointerDown{}, dragging{};
    bool ownsMessageLoop{true};
    void (__stdcall* clicked)(){};
    std::ofstream log;

    UINT CurrentMonitorDpi() const
    {
        // A newly created, hidden PMv2 HWND gets the current monitor DPI.
        // The resident HWND's GetDpiForWindow can still contain its old value
        // while Windows delivers display-setting notifications. No OS setting
        // is changed and the probe is never shown or activated.
        RECT rect{}; GetWindowRect(window, &rect);
        auto previous = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        auto probe = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, L"STATIC", L"",
            WS_POPUP, (rect.left+rect.right)/2, (rect.top+rect.bottom)/2, 1, 1,
            nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
        UINT result = probe ? GetDpiForWindow(probe) : GetDpiForWindow(window);
        if (probe) DestroyWindow(probe);
        if (previous) SetThreadDpiAwarenessContext(previous);
        return result ? result : dpi;
    }
    void ApplyDpi(UINT value, int x, int y)
    {
        dpi = value ? value : 96;
        if (receiver) return;
        // Always derive physical size from the design DIP size. Repeated
        // scale changes must not multiply an already-scaled pixel rectangle.
        SetWindowPos(window, nullptr, x, y, MulDiv(widthDip, dpi, 96),
            MulDiv(heightDip, dpi, 96), SWP_NOACTIVATE | SWP_NOZORDER);
        RECT client{}; GetClientRect(window, &client);
        Layout(client.right, client.bottom);
    }
    void ReconcileDpi()
    {
        if (receiver) return;
        UINT actual = CurrentMonitorDpi();
        RECT rect{}; GetWindowRect(window, &rect);
        if (actual != dpi || rect.right-rect.left != MulDiv(widthDip,actual,96) ||
            rect.bottom-rect.top != MulDiv(heightDip,actual,96))
        {
            log << "dpi-reconcile " << dpi << " -> " << actual << std::endl;
            ApplyDpi(actual, rect.left, rect.top);
        }
    }

    void Layout(int width, int height)
    {
        if (receiver || !root) return;
        const float scale = dpi / 96.0f;
        glass.Size({static_cast<float>(width), static_cast<float>(height)});
        tint.Size(glass.Size());
        if (backdrop) backdrop.Properties().InsertScalar(L"GlassBlur.BlurAmount",20.0f*scale);
        int radius = static_cast<int>(24 * scale);
        HRGN region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (!SetWindowRgn(window, region, TRUE)) DeleteObject(region); // Success transfers ownership.
        if (log) log << "layout " << width << " " << height << " dpi " << dpi << std::endl;
    }
    void Initialize(HWND hwnd)
    {
        window = hwnd;
        dpi = GetDpiForWindow(hwnd);
        BOOL enabled = TRUE;
        check_hresult(DwmSetWindowAttribute(window, DWMWA_USE_HOSTBACKDROPBRUSH, &enabled, sizeof(enabled)));
        MARGINS margins{-1,-1,-1,-1};
        check_hresult(DwmExtendFrameIntoClientArea(window,&margins));
        compositor = Compositor();
        auto interop = compositor.as<ABI::Windows::UI::Composition::Desktop::ICompositorDesktopInterop>();
        check_hresult(interop->CreateDesktopWindowTarget(window, TRUE,
            reinterpret_cast<ABI::Windows::UI::Composition::Desktop::IDesktopWindowTarget**>(put_abi(target))));
        root = compositor.CreateContainerVisual();
        root.RelativeSizeAdjustment({1, 1});
        target.Root(root);
        auto effect = make_self<BlurEffect>();
        effect->source = CompositionEffectSourceParameter(L"Backdrop");
        auto brush = compositor.CreateEffectFactory(effect.as<IGraphicsEffect>(), {L"GlassBlur.BlurAmount"}).CreateBrush();
        backdrop = brush;
        brush.SetSourceParameter(L"Backdrop", compositor.CreateHostBackdropBrush());
        glass = compositor.CreateSpriteVisual();
        glass.Brush(brush);
        root.Children().InsertAtTop(glass);
        tint = compositor.CreateSpriteVisual();
        tint.Brush(compositor.CreateColorBrush({90, 255, 255, 255}));
        root.Children().InsertAtTop(tint);
        RECT rect{};
        GetClientRect(window, &rect);
        Layout(rect.right, rect.bottom);
        log << "host-backdrop enabled; blur 20; no-activate; region clipping" << std::endl;
    }
};

LRESULT CALLBACK WindowProc(HWND hwnd, UINT message, WPARAM wp, LPARAM lp)
{
    auto host = reinterpret_cast<Host*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (message == WM_NCCREATE)
    {
        host = static_cast<Host*>(reinterpret_cast<CREATESTRUCTW*>(lp)->lpCreateParams);
        host->window = hwnd;
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(host));
    }
    if (!host) return DefWindowProcW(hwnd, message, wp, lp);
    switch (message)
    {
    case WM_MOUSEACTIVATE: if (!host->receiver) return MA_NOACTIVATE; break;
    case WM_ERASEBKGND: return 1;
    case WM_SIZE: host->Layout(LOWORD(lp), HIWORD(lp)); return 0;
    case WM_DPICHANGED:
    {
        auto r = reinterpret_cast<RECT*>(lp);
        host->log << "wm-dpichanged " << LOWORD(wp) << std::endl;
        if (host->receiver)
            SetWindowPos(hwnd, nullptr, r->left, r->top, r->right-r->left, r->bottom-r->top, SWP_NOACTIVATE | SWP_NOZORDER);
        else host->ApplyDpi(LOWORD(wp),r->left,r->top);
        return 0;
    }
    case WM_DISPLAYCHANGE:
    case WM_SETTINGCHANGE:
        host->ReconcileDpi();
        // One bounded follow-up handles a broadcast arriving before the new
        // monitor DPI is committed. There is no permanent polling timer.
        if (!host->receiver) SetTimer(hwnd,1,300,nullptr);
        break;
    case WM_TIMER:
        if (wp==1) { KillTimer(hwnd,1); host->ReconcileDpi(); return 0; }
        break;
    case WM_LBUTTONDOWN:
        if (host->receiver)
        {
            host->clicks++;
            SetWindowTextW(hwnd, (L"GlassDesk P0 Receiver — clicks " + std::to_wstring(host->clicks)).c_str());
            host->log << "receiver-click " << host->clicks << " " << GET_X_LPARAM(lp) << " " << GET_Y_LPARAM(lp) << std::endl;
        }
        else
        {
            host->press={GET_X_LPARAM(lp),GET_Y_LPARAM(lp)};
            ClientToScreen(hwnd,&host->press); // Preserve the down-event point even when input is queued.
            RECT rect{}; GetWindowRect(hwnd, &rect);
            host->origin = {rect.left, rect.top};
            host->pointerDown = true; host->dragging = false;
            SetCapture(hwnd);
            host->log << "pointer-down foreground " << reinterpret_cast<uintptr_t>(GetForegroundWindow()) << std::endl;
        }
        return 0;
    case WM_MOUSEMOVE:
        if (host->pointerDown)
        {
            POINT current{}; GetCursorPos(&current);
            int dx = current.x - host->press.x, dy = current.y - host->press.y;
            const float scale = host->dpi / 96.0f;
            if (dx*dx + dy*dy > 36*scale*scale) host->dragging = true;
            if (host->dragging) SetWindowPos(hwnd, nullptr, host->origin.x+dx, host->origin.y+dy, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        return 0;
    case WM_LBUTTONUP:
        if (!host->receiver)
        {
            ReleaseCapture(); host->pointerDown = false;
            host->log << (host->dragging ? "drag-release" : "host-click") << " foreground " << reinterpret_cast<uintptr_t>(GetForegroundWindow()) << std::endl;
            if (!host->dragging && host->clicked) host->clicked();
        }
        return 0;
    case WM_CAPTURECHANGED: host->pointerDown = false; return 0;
    case WM_PAINT:
        if (host->receiver)
        {
            PAINTSTRUCT paint{}; auto dc = BeginPaint(hwnd, &paint);
            RECT rect{}; GetClientRect(hwnd, &rect);
            const COLORREF colors[]{RGB(40,100,205),RGB(242,116,50),RGB(245,224,99),RGB(97,194,176)};
            for(int y=0;y<rect.bottom;y+=24) for(int x=0;x<rect.right;x+=24)
            {
                auto brush = CreateSolidBrush(colors[((x/24)+(y/24))%4]);
                RECT cell{x,y,x+24,y+24}; FillRect(dc,&cell,brush); DeleteObject(brush);
            }
            EndPaint(hwnd, &paint);
            return 0;
        }
        else { PAINTSTRUCT paint{}; BeginPaint(hwnd, &paint); EndPaint(hwnd, &paint); return 0; }
    case WM_CLOSE: DestroyWindow(hwnd); return 0;
    case WM_DESTROY: KillTimer(hwnd,1); if (host->ownsMessageLoop) PostQuitMessage(0); return 0;
    }
    return DefWindowProcW(hwnd, message, wp, lp);
}

#ifndef GLASSDESK_P0_DLL
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR command, int)
{
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    init_apartment(apartment_type::single_threaded);
    Windows::System::DispatcherQueueController queue{nullptr};
    DispatcherQueueOptions options{sizeof(DispatcherQueueOptions),DQTYPE_THREAD_CURRENT,DQTAT_COM_STA};
    check_hresult(CreateDispatcherQueueController(options,
        reinterpret_cast<ABI::Windows::System::IDispatcherQueueController**>(put_abi(queue))));
    Host host;
    host.receiver = wcsstr(command, L"--receiver") != nullptr;
    wchar_t module[MAX_PATH]{}; GetModuleFileNameW(nullptr,module,MAX_PATH);
    host.log.open(std::filesystem::path(module).parent_path() / (host.receiver ? L"receiver.log" : L"glass-host.log"),std::ios::app);
    WNDCLASSW cls{}; cls.hInstance = instance; cls.lpfnWndProc = WindowProc;
    cls.lpszClassName = L"GlassDesk.P0.Host"; cls.hCursor = LoadCursor(nullptr,IDC_ARROW);
    RegisterClassW(&cls);
    auto scale = GetDpiForSystem()/96.0f;
    const DWORD extended = host.receiver ? WS_EX_TOOLWINDOW : WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP;
    HWND hwnd = CreateWindowExW(extended, cls.lpszClassName,
        host.receiver ? L"GlassDesk P0 Receiver — clicks 0" : L"GlassDesk P0 Glass Host",
        host.receiver ? WS_OVERLAPPEDWINDOW : WS_POPUP,
        static_cast<int>((host.receiver?120:240)*scale),static_cast<int>((host.receiver?180:260)*scale),
        static_cast<int>((host.receiver?680:360)*scale),static_cast<int>((host.receiver?350:132)*scale),
        nullptr,nullptr,instance,&host);
    if (!hwnd) return 1;
    try { if (!host.receiver) host.Initialize(hwnd); }
    catch (hresult_error const& error)
    {
        host.log << "composition-error " << std::hex << static_cast<unsigned>(error.code()) << std::endl;
        DestroyWindow(hwnd); return 2;
    }
    if (wcsstr(command,L"--dpi-regression"))
    {
        bool passed=true;
        for(UINT value : {96u,120u,144u,192u,96u,144u})
        {
            RECT suggested{240,260,600,392};
            SendMessageW(hwnd,WM_DPICHANGED,MAKELONG(value,value),reinterpret_cast<LPARAM>(&suggested));
            RECT actual{}; GetClientRect(hwnd,&actual);
            passed &= actual.right==MulDiv(Host::widthDip,value,96) && actual.bottom==MulDiv(Host::heightDip,value,96) && host.dpi==value;
        }
        // Reproduce a resident host stuck at 125% while the monitor is 150%.
        host.ApplyDpi(120,240,260);
        SendMessageW(hwnd,WM_SETTINGCHANGE,0,0);
        const auto actualDpi=host.CurrentMonitorDpi();
        RECT actual{}; GetClientRect(hwnd,&actual);
        passed &= host.dpi==actualDpi && actual.right==MulDiv(Host::widthDip,actualDpi,96) && actual.bottom==MulDiv(Host::heightDip,actualDpi,96);
        std::ofstream report(std::filesystem::path(module).parent_path()/L"dpi-regression.json");
        report << "{\"passed\":" << (passed?"true":"false") << ",\"monitorDpi\":" << actualDpi
            << ",\"width\":" << actual.right << ",\"height\":" << actual.bottom
            << ",\"systemSettingsChanged\":false,\"testKind\":\"in-process message regression\"}";
        DestroyWindow(hwnd);
    }
    else ShowWindow(hwnd,SW_SHOWNOACTIVATE);
    MSG message{}; while(GetMessageW(&message,nullptr,0,0)>0) { TranslateMessage(&message); DispatchMessageW(&message); }
    if (host.target) host.target.Root(nullptr);
    host.glass = nullptr; host.tint = nullptr; host.backdrop=nullptr; host.root = nullptr; host.target = nullptr; host.compositor = nullptr;
    host.log << "clean-shutdown" << std::endl;
    auto shutdown = queue.ShutdownQueueAsync();
    while (shutdown.Status() == winrt::Windows::Foundation::AsyncStatus::Started)
    {
        while (PeekMessageW(&message,nullptr,0,0,PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        MsgWaitForMultipleObjectsEx(0,nullptr,10,QS_ALLINPUT,MWMO_INPUTAVAILABLE);
    }
    return 0;
}
#else
struct ProbeContext
{
    Host host;
    Windows::System::DispatcherQueueController queue{nullptr};
};
extern "C" __declspec(dllexport) HRESULT __stdcall GDProbeCreate(void (__stdcall* callback)(), void** context, HWND* result)
{
    try
    {
        auto probe = std::make_unique<ProbeContext>();
        DispatcherQueueOptions options{sizeof(DispatcherQueueOptions),DQTYPE_THREAD_CURRENT,DQTAT_COM_NONE};
        check_hresult(CreateDispatcherQueueController(options,
            reinterpret_cast<ABI::Windows::System::IDispatcherQueueController**>(put_abi(probe->queue))));
        probe->host.ownsMessageLoop = false;
        probe->host.clicked = callback;
        wchar_t module[MAX_PATH]{}; GetModuleFileNameW(nullptr,module,MAX_PATH);
        probe->host.log.open(std::filesystem::path(module).parent_path()/L"lifecycle-native.log",std::ios::app);
        WNDCLASSW cls{}; cls.hInstance = GetModuleHandleW(nullptr); cls.lpfnWndProc = WindowProc;
        cls.lpszClassName=L"GlassDesk.P0.LifecycleHost"; cls.hCursor=LoadCursor(nullptr,IDC_ARROW);
        RegisterClassW(&cls);
        const float scale=GetDpiForSystem()/96.0f;
        auto hwnd=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TOPMOST|WS_EX_NOREDIRECTIONBITMAP,
            cls.lpszClassName,L"GlassDesk P0 Lifecycle Host",WS_POPUP,
            int(240*scale),int(380*scale),int(360*scale),int(132*scale),nullptr,nullptr,cls.hInstance,&probe->host);
        check_bool(hwnd!=nullptr);
        probe->host.Initialize(hwnd);
        ShowWindow(hwnd,SW_SHOWNOACTIVATE);
        *result=hwnd; *context=probe.release(); return S_OK;
    } catch(...) { return to_hresult(); }
}
extern "C" __declspec(dllexport) void __stdcall GDProbeDestroy(void* context)
{
    auto probe=std::unique_ptr<ProbeContext>(static_cast<ProbeContext*>(context));
    if (!probe) return;
    if (probe->host.target) probe->host.target.Root(nullptr);
    probe->host.glass=nullptr; probe->host.tint=nullptr; probe->host.backdrop=nullptr; probe->host.root=nullptr;
    probe->host.target=nullptr; probe->host.compositor=nullptr;
    if(IsWindow(probe->host.window)) DestroyWindow(probe->host.window);
    probe->host.log << "clean-shutdown" << std::endl;
    auto shutdown=probe->queue.ShutdownQueueAsync();
    MSG message{};
    while(shutdown.Status()==Windows::Foundation::AsyncStatus::Started)
    {
        while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)) {TranslateMessage(&message);DispatchMessageW(&message);}
        MsgWaitForMultipleObjectsEx(0,nullptr,10,QS_ALLINPUT,MWMO_INPUTAVAILABLE);
    }
}
#endif
