#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <windowsx.h>
#include <dwmapi.h>
#include <DispatcherQueue.h>
#include <d3d11.h>
#include <d3d11_4.h>
#include <dxgi.h>
#include <d2d1_1.h>
#include <d2d1effects.h>
#include <dwrite.h>
#include <wincodec.h>
#include <windows.ui.composition.interop.h>
#include <windows.graphics.effects.interop.h>
#include <windows.graphics.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Graphics.Effects.h>
#include <winrt/Windows.Graphics.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.System.h>
#include <winrt/Windows.UI.Composition.Desktop.h>
#include <cmath>
#include <memory>
#include <vector>
#include <list>
#include <string>
#include <algorithm>
#include <limits>
#include "NativeApi.h"
#include "Accessibility.h"
#include "LiquidGlass.h"
#include "TypographyEngine.h"

using namespace winrt;
using namespace winrt::Windows::Foundation;
using namespace winrt::Windows::Graphics::Effects;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::UI::Composition;
using namespace winrt::Windows::UI::Composition::Desktop;
using EffectInterop = ABI::Windows::Graphics::Effects::IGraphicsEffectD2D1Interop;

namespace gd
{
constexpr UINT DpiTimer=71;
constexpr UINT LiquidTimer=72;
constexpr UINT RecoveredMessage=WM_APP+74;
constexpr int ContextEvent=100;
constexpr wchar_t WindowClass[]=L"GlassDesk.Native.Host.0.1";
constexpr wchar_t ShadowClass[]=L"GlassDesk.Native.Shadow.0.1";

struct MaterialEffect : implements<MaterialEffect,IGraphicsEffect,IGraphicsEffectSource,EffectInterop>
{
    bool grain{};
    hstring name{L"GlassBlur"};
    IGraphicsEffectSource source{nullptr};
    hstring Name()const{return name;}
    void Name(hstring const& value){name=value;}
    HRESULT __stdcall GetEffectId(GUID* result)noexcept override
    {if(!result)return E_POINTER;*result=grain?CLSID_D2D1Border:CLSID_D2D1GaussianBlur;return S_OK;}
    HRESULT __stdcall GetNamedPropertyMapping(LPCWSTR value,UINT* index,
        ABI::Windows::Graphics::Effects::GRAPHICS_EFFECT_PROPERTY_MAPPING* mapping)noexcept override
    {
        if(!value||!index||!mapping)return E_POINTER;
        if(grain||wcscmp(value,L"BlurAmount"))return E_INVALIDARG;
        *index=D2D1_GAUSSIANBLUR_PROP_STANDARD_DEVIATION;
        *mapping=ABI::Windows::Graphics::Effects::GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT;return S_OK;
    }
    HRESULT __stdcall GetPropertyCount(UINT* result)noexcept override {if(!result)return E_POINTER;*result=grain?2:3;return S_OK;}
    HRESULT __stdcall GetProperty(UINT index,ABI::Windows::Foundation::IPropertyValue** result)noexcept override
    {
        if(!result)return E_POINTER;*result=nullptr;
        try
        {
            IPropertyValue value{nullptr};
            if(grain)
            {if(index>1)return E_INVALIDARG;value=PropertyValue::CreateUInt32(D2D1_BORDER_EDGE_MODE_WRAP).as<IPropertyValue>();}
            else if(index==0)value=PropertyValue::CreateSingle(20).as<IPropertyValue>();
            else if(index==1)value=PropertyValue::CreateUInt32(D2D1_GAUSSIANBLUR_OPTIMIZATION_BALANCED).as<IPropertyValue>();
            else if(index==2)value=PropertyValue::CreateUInt32(D2D1_BORDER_MODE_HARD).as<IPropertyValue>();
            else return E_INVALIDARG;
            *result=static_cast<ABI::Windows::Foundation::IPropertyValue*>(detach_abi(value));return S_OK;
        }catch(...){return to_hresult();}
    }
    HRESULT __stdcall GetSource(UINT index,ABI::Windows::Graphics::Effects::IGraphicsEffectSource** result)noexcept override
    {if(!result)return E_POINTER;*result=nullptr;if(index)return E_INVALIDARG;auto copy=source;*result=static_cast<ABI::Windows::Graphics::Effects::IGraphicsEffectSource*>(detach_abi(copy));return S_OK;}
    HRESULT __stdcall GetSourceCount(UINT* result)noexcept override {if(!result)return E_POINTER;*result=1;return S_OK;}
};

struct ImageEntry {std::vector<unsigned char> bytes;com_ptr<ID2D1Bitmap1> bitmap;};

// Retain identical segment topology for GPU path interpolation, including
// collapsed polygons. No playback state or icon policy lives in this bridge.
com_ptr<ID2D1PathGeometry> RoundedPolygon(ID2D1Factory* factory,std::vector<GDPoint> const& points,float rounding)
{
    com_ptr<ID2D1PathGeometry> path;check_hresult(factory->CreatePathGeometry(path.put()));
    com_ptr<ID2D1GeometrySink> sink;check_hresult(path->Open(sink.put()));
    std::vector<D2D1_POINT_2F> incoming(points.size()),outgoing(points.size());
    for(size_t i=0;i<points.size();++i)
    {
        auto p=points[i],previous=points[(i+points.size()-1)%points.size()],next=points[(i+1)%points.size()];
        float before=std::hypot(previous.x-p.x,previous.y-p.y),after=std::hypot(next.x-p.x,next.y-p.y);
        float cut=std::min(rounding,std::min(before,after)*.4f);
        float a=before>0?cut/before:0,b=after>0?cut/after:0;
        incoming[i]={p.x+(previous.x-p.x)*a,p.y+(previous.y-p.y)*a};
        outgoing[i]={p.x+(next.x-p.x)*b,p.y+(next.y-p.y)*b};
    }
    sink->BeginFigure(incoming[0],D2D1_FIGURE_BEGIN_FILLED);
    for(size_t i=0;i<points.size();++i)
    {
        auto p=points[i];auto a=incoming[i],b=outgoing[i];
        sink->AddBezier({{a.x+(p.x-a.x)*2/3,a.y+(p.y-a.y)*2/3},
            {b.x+(p.x-b.x)*2/3,b.y+(p.y-b.y)*2/3},b});
        sink->AddLine(incoming[(i+1)%points.size()]);
    }
    sink->EndFigure(D2D1_FIGURE_END_CLOSED);check_hresult(sink->Close());return path;
}

struct GeometrySource : implements<GeometrySource,winrt::Windows::Graphics::IGeometrySource2D,ABI::Windows::Graphics::IGeometrySource2DInterop>
{
    std::vector<GDPoint> points;
    float rounding;
    com_ptr<ID2D1PathGeometry> geometry;
    GeometrySource(ID2D1Factory* factory,std::vector<GDPoint> value,float radius)
        :points(std::move(value)),rounding(radius),geometry(RoundedPolygon(factory,points,radius)){}
    HRESULT __stdcall GetGeometry(ID2D1Geometry** value)noexcept override
    {if(!value)return E_POINTER;*value=geometry.get();(*value)->AddRef();return S_OK;}
    HRESULT __stdcall TryGetGeometryUsingFactory(ID2D1Factory* factory,ID2D1Geometry** value)noexcept override
    {
        if(!factory||!value)return E_POINTER;*value=nullptr;
        try{RoundedPolygon(factory,points,rounding).as<ID2D1Geometry>().copy_to(value);return S_OK;}catch(...){return to_hresult();}
    }
};

struct Host : std::enable_shared_from_this<Host>
{
    DWORD ownerThread{GetCurrentThreadId()};
    HWND window{},shadow{};
    GDCallback callback{};void* user{};
    GDFrame frame{232,48,24,20,.24f,0};
    unsigned dpi{96}; bool visible{},tracking{},captured{},destroying{},fallback{};
    int cachedWidth{},cachedHeight{};float cachedRadius{-1};unsigned cachedDpi{};
    winrt::Windows::System::DispatcherQueueController queue{nullptr};
    Compositor compositor{nullptr}; DesktopWindowTarget target{nullptr};
    ContainerVisual root{nullptr},content{nullptr};
    SpriteVisual glass{nullptr},tint{nullptr},grain{nullptr};
    ShapeVisual border{nullptr};CompositionRoundedRectangleGeometry clipGeometry{nullptr},borderGeometry{nullptr};
    CompositionSpriteShape borderShape{nullptr};
    CompositionEffectBrush backdrop{nullptr};
    CompositionLinearGradientBrush tintBrush{nullptr},borderBrush{nullptr};
    CompositionColorBrush solidTintBrush{nullptr},solidBorderBrush{nullptr};
    float materialTint{-1},materialBlur{-1};
    COLORREF materialWindowColor{CLR_INVALID},materialBorderColor{CLR_INVALID};
    bool materialApplied{},materialSolid{},materialGrainVisible{};
    CompositionGraphicsDevice graphics{nullptr};CompositionDrawingSurface grainSurface{nullptr};
    com_ptr<ID3D11Device> d3d;com_ptr<ID3D11DeviceContext> d3dContext;
    com_ptr<ID2D1Factory1> d2dFactory;com_ptr<ID2D1Device> d2d;com_ptr<ID2D1DeviceContext> cacheContext;
    com_ptr<ID2D1Factory> geometryFactory;
    com_ptr<IDWriteFactory> textFactory;com_ptr<IWICImagingFactory> wic;
    std::unique_ptr<typography::Engine> typographyEngine;
    bool liquidEnabled{},liquidReady{};
    std::unique_ptr<LiquidGlass> liquid;
    CompositionSurfaceBrush liquidBrush{nullptr};CompositionDrawingSurface liquidSurface{nullptr};
    std::list<ImageEntry> images;
    HDC shadowDc{};HBITMAP shadowBitmap{};HGDIOBJ previousBitmap{};void* shadowBits{};
    int shadowWidth{},shadowHeight{};float shadowRadius{-1};unsigned shadowDpi{};
    int shadowX{std::numeric_limits<int>::min()},shadowY{std::numeric_limits<int>::min()};bool shadowShown{};
    std::shared_ptr<AccessibleState> accessibility=std::make_shared<AccessibleState>();
    ComPtr<ElementProvider> provider;

    ~Host(){Shutdown();}
    float Scale()const{return dpi/96.0f;}
    HRESULT CheckThread()const{return ownerThread==GetCurrentThreadId()?S_OK:RPC_E_WRONG_THREAD;}
    void Emit(int kind,float x=0,float y=0,int screenX=0,int screenY=0,int control=0,double value=0)noexcept
    {
        if(callback&&!destroying){GDPointer event{kind,x,y,screenX,screenY,control,value};callback(user,&event);}
    }
    void EmitPointer(int kind,LPARAM point,double value=0)
    {
        POINT screen{GET_X_LPARAM(point),GET_Y_LPARAM(point)};ClientToScreen(window,&screen);
        auto x=GET_X_LPARAM(point)/Scale(),y=GET_Y_LPARAM(point)/Scale();
        Emit(kind,x,y,screen.x,screen.y,Hit(x,y),value);
    }
    int Hit(float x,float y)
    {
        std::lock_guard lock(accessibility->mutex);
        for(auto item=accessibility->items.rbegin();item!=accessibility->items.rend();++item)
            if(x>=item->x&&y>=item->y&&x<item->x+item->width&&y<item->y+item->height)return item->id;
        return 0;
    }
    bool Inside(float x,float y)const
    {
        if(x<0||y<0||x>=frame.width||y>=frame.height)return false;
        auto radius=std::min(frame.radius,std::min(frame.width,frame.height)/2);
        auto cx=std::clamp(x,radius,frame.width-radius),cy=std::clamp(y,radius,frame.height-radius);
        auto dx=x-cx,dy=y-cy;return dx*dx+dy*dy<=radius*radius;
    }
    GDContext Context()const
    {RECT rectangle{};GetWindowRect(window,&rectangle);return {rectangle.left,rectangle.top,frame.width,frame.height,dpi};}
    void UpdateAccessibleGeometry()
    {
        RECT rectangle{};GetWindowRect(window,&rectangle);
        std::lock_guard lock(accessibility->mutex);
        accessibility->rectangle=rectangle;accessibility->dpi=dpi;accessibility->visible=visible;
    }
    unsigned CurrentMonitorDpi()const
    {
        RECT rect{};GetWindowRect(window,&rect);
        auto previous=SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        auto probe=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,L"STATIC",L"",WS_POPUP,
            (rect.left+rect.right)/2,(rect.top+rect.bottom)/2,1,1,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        auto value=probe?GetDpiForWindow(probe):GetDpiForWindow(window);
        if(probe)DestroyWindow(probe);if(previous)SetThreadDpiAwarenessContext(previous);
        return value?value:dpi;
    }
    void ApplyDpi(unsigned value,int x,int y)
    {
        dpi=value?value:96;
        SetWindowPos(window,nullptr,x,y,int(std::lround(frame.width*Scale())),int(std::lround(frame.height*Scale())),SWP_NOACTIVATE|SWP_NOZORDER);
        Layout();UpdateAccessibleGeometry();Emit(9);
    }
    void ReconcileDpi()
    {
        auto value=CurrentMonitorDpi();RECT rectangle{};GetWindowRect(window,&rectangle);
        // The resident GetDpiForWindow can lag a display-settings broadcast.
        // Keep the presented DIP frame (including an interrupted animation).
        if(value!=dpi||rectangle.right-rectangle.left!=int(std::lround(frame.width*value/96))||rectangle.bottom-rectangle.top!=int(std::lround(frame.height*value/96)))
            ApplyDpi(value,rectangle.left,rectangle.top);
    }
    void CreateGraphics()
    {
        if(liquid){liquid.reset();liquidReady=false;liquidSurface=nullptr;liquidBrush=nullptr;if(glass)glass.Brush(backdrop);}
        images.clear();cacheContext=nullptr;d2d=nullptr;d3dContext=nullptr;d3d=nullptr;
        UINT flags=D3D11_CREATE_DEVICE_BGRA_SUPPORT;D3D_FEATURE_LEVEL level{};
        auto hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,flags,nullptr,0,D3D11_SDK_VERSION,d3d.put(),&level,d3dContext.put());
        if(FAILED(hr))check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,flags,nullptr,0,D3D11_SDK_VERSION,d3d.put(),&level,d3dContext.put()));
        d3dContext.as<ID3D11Multithread>()->SetMultithreadProtected(TRUE);
        if(!d2dFactory)check_hresult(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,__uuidof(ID2D1Factory1),nullptr,d2dFactory.put_void()));
        auto dxgi=d3d.as<IDXGIDevice>();check_hresult(d2dFactory->CreateDevice(dxgi.get(),d2d.put()));
        check_hresult(d2d->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE,cacheContext.put()));
        if(graphics)check_hresult(graphics.as<ABI::Windows::UI::Composition::ICompositionGraphicsDeviceInterop>()->SetRenderingDevice(d2d.get()));
        else check_hresult(compositor.as<ABI::Windows::UI::Composition::ICompositorInterop>()->CreateGraphicsDevice(d2d.get(),reinterpret_cast<ABI::Windows::UI::Composition::ICompositionGraphicsDevice**>(put_abi(graphics))));
        if(!textFactory)check_hresult(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED,__uuidof(IDWriteFactory),reinterpret_cast<::IUnknown**>(textFactory.put())));
        if(!wic)check_hresult(CoCreateInstance(CLSID_WICImagingFactory2,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(wic.put())));
        if(shadow){liquid=std::make_unique<LiquidGlass>(window,shadow,d3d.get(),d3dContext.get(),graphics);liquid->SetEnabled(liquidEnabled);}
    }
    static D2D1_COLOR_F Color(unsigned argb)
    {return D2D1::ColorF(((argb>>16)&255)/255.0f,((argb>>8)&255)/255.0f,(argb&255)/255.0f,((argb>>24)&255)/255.0f);}
    CompositionDrawingSurface CreateSurface(int width,int height)
    {return graphics.CreateDrawingSurface({float(width),float(height)},DirectXPixelFormat::B8G8R8A8UIntNormalized,DirectXAlphaMode::Premultiplied);}
    template<typename Painter> void Draw(CompositionDrawingSurface const& surface,float targetDpi,Painter paint)
    {
        auto interop=surface.as<ABI::Windows::UI::Composition::ICompositionDrawingSurfaceInterop>();
        com_ptr<ID2D1DeviceContext> context;POINT offset{};
        check_hresult(interop->BeginDraw(nullptr,__uuidof(ID2D1DeviceContext),context.put_void(),&offset));
        try
        {
            context->SetDpi(targetDpi,targetDpi);
            context->SetTransform(D2D1::Matrix3x2F::Translation(offset.x*96.0f/targetDpi,offset.y*96.0f/targetDpi));
            context->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
            context->Clear(D2D1::ColorF(0,0,0,0));paint(context.get());
        }
        catch(...){interop->EndDraw();throw;}
        check_hresult(interop->EndDraw());
    }
    void CreateGrain()
    {
        grainSurface=CreateSurface(64,64);
        Draw(grainSurface,96,[&](ID2D1DeviceContext* context)
        {
            // The opaque, fine-grained tile contributes less than 0.2% of
            // luminance variation after compositing. It must not compete
            // with small text or reveal the repeating texture.
            com_ptr<ID2D1SolidColorBrush> brush;check_hresult(context->CreateSolidColorBrush(D2D1::ColorF(.5f,.5f,.5f,1),brush.put()));
            unsigned random=0x91E10DA5;
            context->SetAntialiasMode(D2D1_ANTIALIAS_MODE_ALIASED);
            for(int y=0;y<64;++y)for(int x=0;x<64;++x)
            {random=random*1664525+1013904223;float gray=.42f+.16f*(random>>24)/255.0f;brush->SetColor(D2D1::ColorF(gray,gray,gray,1));context->FillRectangle(D2D1::RectF(float(x),float(y),float(x+1),float(y+1)),brush.get());}
        });
        auto effect=make_self<MaterialEffect>();effect->grain=true;effect->name=L"GlassGrain";effect->source=CompositionEffectSourceParameter(L"Texture");
        auto brush=compositor.CreateEffectFactory(effect.as<IGraphicsEffect>()).CreateBrush();
        auto texture=compositor.CreateSurfaceBrush(grainSurface);texture.Stretch(CompositionStretch::None);
        brush.SetSourceParameter(L"Texture",texture);grain.Brush(brush);grain.Opacity(.012f);
    }
    void Initialize(HWND hwnd)
    {
        window=hwnd;dpi=GetDpiForWindow(hwnd);if(!dpi)dpi=96;
        BOOL enabled=TRUE;auto backdropResult=DwmSetWindowAttribute(hwnd,DWMWA_USE_HOSTBACKDROPBRUSH,&enabled,sizeof(enabled));
        MARGINS margins{-1,-1,-1,-1};auto frameResult=DwmExtendFrameIntoClientArea(hwnd,&margins);
        fallback=FAILED(backdropResult)||FAILED(frameResult);
        compositor=Compositor();
        check_hresult(compositor.as<ABI::Windows::UI::Composition::Desktop::ICompositorDesktopInterop>()->CreateDesktopWindowTarget(hwnd,TRUE,reinterpret_cast<ABI::Windows::UI::Composition::Desktop::IDesktopWindowTarget**>(put_abi(target))));
        root=compositor.CreateContainerVisual();root.RelativeSizeAdjustment({1,1});target.Root(root);
        clipGeometry=compositor.CreateRoundedRectangleGeometry();root.Clip(compositor.CreateGeometricClip(clipGeometry));
        auto effect=make_self<MaterialEffect>();effect->source=CompositionEffectSourceParameter(L"Backdrop");
        backdrop=compositor.CreateEffectFactory(effect.as<IGraphicsEffect>(),{L"GlassBlur.BlurAmount"}).CreateBrush();
        backdrop.SetSourceParameter(L"Backdrop",compositor.CreateHostBackdropBrush());
        glass=compositor.CreateSpriteVisual();glass.Brush(backdrop);root.Children().InsertAtTop(glass);
        // The real host backdrop supplies the material color and blur. Keep
        // its tint to a single veil at the caller's exact opacity; edge light
        // belongs to the thin border rather than another white shell layer.
        tintBrush=compositor.CreateLinearGradientBrush();tintBrush.MappingMode(CompositionMappingMode::Relative);
        tintBrush.StartPoint({0,0});tintBrush.EndPoint({0,1});
        tintBrush.ColorStops().Append(compositor.CreateColorGradientStop(0,{0,255,255,255}));
        tintBrush.ColorStops().Append(compositor.CreateColorGradientStop(.45f,{0,250,251,252}));
        tintBrush.ColorStops().Append(compositor.CreateColorGradientStop(1,{0,246,248,250}));
        solidTintBrush=compositor.CreateColorBrush();solidBorderBrush=compositor.CreateColorBrush();
        tint=compositor.CreateSpriteVisual();tint.Brush(tintBrush);root.Children().InsertAtTop(tint);
        grain=compositor.CreateSpriteVisual();root.Children().InsertAtTop(grain);
        border=compositor.CreateShapeVisual();borderGeometry=compositor.CreateRoundedRectangleGeometry();borderShape=compositor.CreateSpriteShape(borderGeometry);
        borderBrush=compositor.CreateLinearGradientBrush();borderBrush.MappingMode(CompositionMappingMode::Relative);
        borderBrush.StartPoint({0,0});borderBrush.EndPoint({0,1});
        borderBrush.ColorStops().Append(compositor.CreateColorGradientStop(0,{88,255,255,255}));
        borderBrush.ColorStops().Append(compositor.CreateColorGradientStop(.24f,{24,255,255,255}));
        borderBrush.ColorStops().Append(compositor.CreateColorGradientStop(1,{14,47,54,65}));
        borderShape.StrokeBrush(borderBrush);border.Shapes().Append(borderShape);root.Children().InsertAtTop(border);
        content=compositor.CreateContainerVisual();root.Children().InsertAtTop(content);
        try{CreateGraphics();CreateGrain();}catch(...){fallback=true;}
        shadow=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TOPMOST|WS_EX_TRANSPARENT|WS_EX_LAYERED,
            ShadowClass,L"GlassDesk Shadow",WS_POPUP,0,0,1,1,nullptr,nullptr,GetModuleHandleW(nullptr),this);
        check_bool(shadow!=nullptr);
        if(d3d&&graphics)liquid=std::make_unique<LiquidGlass>(window,shadow,d3d.get(),d3dContext.get(),graphics);
        provider=MakeElement(accessibility,0,true);
        {std::lock_guard lock(accessibility->mutex);accessibility->hwnd=hwnd;accessibility->root=static_cast<IRawElementProviderFragment*>(provider.Get());}
        SetWindowPos(window,nullptr,0,0,int(std::lround(frame.width*Scale())),int(std::lround(frame.height*Scale())),SWP_NOMOVE|SWP_NOACTIVATE|SWP_NOZORDER);
        Layout();UpdateAccessibleGeometry();
    }
    void ApplyMaterial()
    {
        if(!tint)return;
        bool solid=frame.solid||fallback;
        bool grainVisible=!solid&&grainSurface!=nullptr;
        if(!materialApplied||solid!=materialSolid)
        {
            glass.IsVisible(!solid);
            tint.Brush(solid?solidTintBrush.as<CompositionBrush>():tintBrush.as<CompositionBrush>());
            borderShape.StrokeBrush(solid?solidBorderBrush.as<CompositionBrush>():borderBrush.as<CompositionBrush>());
        }
        if(!materialApplied||grainVisible!=materialGrainVisible)grain.IsVisible(grainVisible);
        if(solid)
        {
            auto rgb=GetSysColor(COLOR_WINDOW);
            auto edge=GetSysColor(COLOR_WINDOWFRAME);
            if(rgb!=materialWindowColor)
            {solidTintBrush.Color({255,GetRValue(rgb),GetGValue(rgb),GetBValue(rgb)});materialWindowColor=rgb;}
            if(edge!=materialBorderColor)
            {solidBorderBrush.Color({255,GetRValue(edge),GetGValue(edge),GetBValue(edge)});materialBorderColor=edge;}
        }
        else
        {
            auto opacity=std::clamp(frame.tint,0.0f,1.0f);
            if(opacity!=materialTint)
            {
                auto alpha=[](float value){return static_cast<unsigned char>(std::lround(std::clamp(value,0.0f,1.0f)*255));};
                // Do not silently increase white coverage above TintOpacity:
                // wallpaper hues must remain visible through the frosted shell.
                auto tintAlpha=alpha(opacity);
                tintBrush.ColorStops().GetAt(0).Color({tintAlpha,255,255,255});
                tintBrush.ColorStops().GetAt(1).Color({tintAlpha,250,251,252});
                tintBrush.ColorStops().GetAt(2).Color({tintAlpha,246,248,250});
                materialTint=opacity;
            }
            auto blur=std::max(0.0f,frame.blur)*Scale();
            if(blur!=materialBlur)
            {backdrop.Properties().InsertScalar(L"GlassBlur.BlurAmount",blur);materialBlur=blur;}
        }
        materialSolid=solid;materialGrainVisible=grainVisible;materialApplied=true;
    }
    void Layout()
    {
        if(!root)return;
        RECT client{};GetClientRect(window,&client);int width=client.right,height=client.bottom;
        float scale=Scale(),radius=std::clamp(frame.radius*scale,0.0f,std::min(width,height)/2.0f);
        if(width!=cachedWidth||height!=cachedHeight||radius!=cachedRadius||dpi!=cachedDpi)
        {
            auto size=winrt::Windows::Foundation::Numerics::float2{float(width),float(height)};
            glass.Size(size);tint.Size(size);grain.Size(size);border.Size(size);
            clipGeometry.Size(size);clipGeometry.CornerRadius({radius,radius});
            borderGeometry.Offset({scale*.5f,scale*.5f});borderGeometry.Size({std::max(0.0f,width-scale),std::max(0.0f,height-scale)});
            borderGeometry.CornerRadius({std::max(0.0f,radius-scale*.5f),std::max(0.0f,radius-scale*.5f)});borderShape.StrokeThickness(scale);
            content.Size({frame.width,frame.height});content.Scale({scale,scale,1});
            // The visual boundary remains floating point and antialiased.
            // Give it one physical pixel of rounded HWND coverage so integer
            // region quantization does not amputate partially covered pixels.
            // Corners remain outside the HWND region for cross-process input.
            auto region=CreateRoundRectRgn(-1,-1,width+2,height+2,int(std::ceil(radius*2)),int(std::ceil(radius*2)));
            if(region&&!SetWindowRgn(window,region,TRUE))DeleteObject(region);
            cachedWidth=width;cachedHeight=height;cachedRadius=radius;cachedDpi=dpi;
        }
        // DIP animation dimensions may change while rounded physical size is
        // unchanged; the C# visual tree still receives the exact DIP frame.
        content.Size({frame.width,frame.height});ApplyMaterial();UpdateShadow();UpdateLiquid();
    }
    void UpdateLiquid()
    {
        if(!window||destroying)return;
        bool shouldRun=liquidEnabled&&visible&&!frame.solid&&!fallback;
        if(liquid)
        {
            liquid->SetEnabled(liquidEnabled);liquid->Suspend(!visible,frame.solid||fallback);
            if(shouldRun)
            {
                RECT panel{};GetWindowRect(window,&panel);
                liquid->Tick(panel,std::min(frame.radius*Scale(),std::min(panel.right-panel.left,panel.bottom-panel.top)/2.0f),frame.blur*Scale());
            }
            bool ready=shouldRun&&liquid->Ready();auto surface=ready?liquid->Surface():CompositionDrawingSurface{nullptr};
            if(ready&&surface!=liquidSurface)
            {
                if(!liquidBrush){liquidBrush=compositor.CreateSurfaceBrush();liquidBrush.Stretch(CompositionStretch::Fill);}
                liquidBrush.Surface(surface);liquidSurface=surface;
            }
            if(ready!=liquidReady){glass.Brush(ready?liquidBrush.as<CompositionBrush>():backdrop.as<CompositionBrush>());liquidReady=ready;}
            if(!ready){liquidSurface=nullptr;if(liquidBrush)liquidBrush.Surface(nullptr);}
        }
        if(shouldRun)SetTimer(window,LiquidTimer,34,nullptr);else KillTimer(window,LiquidTimer);
    }
    static float RoundedDistance(float x,float y,float width,float height,float radius)
    {
        float qx=std::abs(x-width/2)-width/2+radius,qy=std::abs(y-height/2)-height/2+radius;
        float px=std::max(qx,0.0f),py=std::max(qy,0.0f);
        return std::sqrt(px*px+py*py)+std::min(std::max(qx,qy),0.0f)-radius;
    }
    void ReleaseShadowBitmap()
    {
        if(shadowDc&&previousBitmap)SelectObject(shadowDc,previousBitmap);
        if(shadowBitmap)DeleteObject(shadowBitmap);if(shadowDc)DeleteDC(shadowDc);
        shadowDc=nullptr;shadowBitmap=nullptr;previousBitmap=nullptr;shadowBits=nullptr;shadowWidth=shadowHeight=0;
    }
    void UpdateShadow()
    {
        if(!shadow||destroying)return;
        RECT rectangle{};GetWindowRect(window,&rectangle);
        int panelWidth=rectangle.right-rectangle.left,panelHeight=rectangle.bottom-rectangle.top;
        if(panelWidth<1||panelHeight<1)return;
        auto scale=Scale();int margin=int(std::ceil(24*scale));
        int width=panelWidth+margin*2,height=panelHeight+margin*2;
        float radius=std::min(frame.radius*scale,std::min(panelWidth,panelHeight)/2.0f);
        bool repaint=width!=shadowWidth||height!=shadowHeight||radius!=shadowRadius||dpi!=shadowDpi;
        int positionX=rectangle.left-margin,positionY=rectangle.top-margin;
        bool moved=positionX!=shadowX||positionY!=shadowY;
        if(!repaint&&!moved&&visible==shadowShown)return;
        if(width!=shadowWidth||height!=shadowHeight)
        {
            ReleaseShadowBitmap();shadowDc=CreateCompatibleDC(nullptr);check_bool(shadowDc!=nullptr);
            BITMAPINFO info{};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);info.bmiHeader.biWidth=width;
            info.bmiHeader.biHeight=-height;info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;info.bmiHeader.biCompression=BI_RGB;
            shadowBitmap=CreateDIBSection(shadowDc,&info,DIB_RGB_COLORS,&shadowBits,nullptr,0);check_bool(shadowBitmap!=nullptr);
            previousBitmap=SelectObject(shadowDc,shadowBitmap);shadowWidth=width;shadowHeight=height;
        }
        if(repaint)
        {
            auto pixels=static_cast<unsigned*>(shadowBits);std::fill(pixels,pixels+size_t(width)*height,0u);
            float sigma=8*scale,offset=4*scale;
            for(int y=0;y<height;++y)for(int x=0;x<width;++x)
            {
                float localX=x-margin+.5f,localY=y-margin+.5f;
                if(RoundedDistance(localX,localY,float(panelWidth),float(panelHeight),radius)<=0)continue;
                float distance=std::max(0.0f,RoundedDistance(localX,localY-offset,float(panelWidth),float(panelHeight),radius));
                auto alpha=static_cast<unsigned>(std::lround(24*std::exp(-distance*distance/(2*sigma*sigma))));
                pixels[size_t(y)*width+x]=alpha<<24; // Premultiplied black, with a transparent center.
            }
            shadowRadius=radius;shadowDpi=dpi;
        }
        POINT position{positionX,positionY},source{};SIZE size{width,height};BLENDFUNCTION blend{AC_SRC_OVER,0,255,AC_SRC_ALPHA};
        if(repaint)check_bool(UpdateLayeredWindow(shadow,nullptr,&position,&size,shadowDc,&source,0,&blend,ULW_ALPHA)!=FALSE);
        SetWindowPos(shadow,window,position.x,position.y,width,height,SWP_NOACTIVATE|(visible?SWP_SHOWWINDOW:SWP_HIDEWINDOW));
        shadowX=positionX;shadowY=positionY;shadowShown=visible;
    }
    com_ptr<ID2D1Bitmap1> Decode(GDOp const& op)
    {
        if(!op.image||!op.imageSize||op.imageSize>32u*1024*1024)return nullptr;
        auto found=std::find_if(images.begin(),images.end(),[&](auto const& entry)
        {return entry.bytes.size()==op.imageSize&&std::equal(entry.bytes.begin(),entry.bytes.end(),op.image);});
        if(found!=images.end()){auto bitmap=found->bitmap;images.splice(images.begin(),images,found);return bitmap;}
        com_ptr<IWICStream> stream;check_hresult(wic->CreateStream(stream.put()));
        check_hresult(stream->InitializeFromMemory(const_cast<BYTE*>(op.image),op.imageSize));
        com_ptr<IWICBitmapDecoder> decoder;check_hresult(wic->CreateDecoderFromStream(stream.get(),nullptr,WICDecodeMetadataCacheOnDemand,decoder.put()));
        com_ptr<IWICBitmapFrameDecode> bitmap;check_hresult(decoder->GetFrame(0,bitmap.put()));
        UINT width{},height{};check_hresult(bitmap->GetSize(&width,&height));
        if(!width||!height||width>65535||height>65535)throw hresult_invalid_argument();
        double ratio=std::min(1.0,768.0/std::max(width,height));
        com_ptr<IWICBitmapSource> source=bitmap.as<IWICBitmapSource>();
        if(ratio<1)
        {com_ptr<IWICBitmapScaler> scaler;check_hresult(wic->CreateBitmapScaler(scaler.put()));check_hresult(scaler->Initialize(bitmap.get(),std::max(1u,unsigned(width*ratio)),std::max(1u,unsigned(height*ratio)),WICBitmapInterpolationModeFant));source=scaler.as<IWICBitmapSource>();}
        com_ptr<IWICFormatConverter> converter;check_hresult(wic->CreateFormatConverter(converter.put()));
        check_hresult(converter->Initialize(source.get(),GUID_WICPixelFormat32bppPBGRA,WICBitmapDitherTypeNone,nullptr,0,WICBitmapPaletteTypeCustom));
        com_ptr<ID2D1Bitmap1> result;check_hresult(cacheContext->CreateBitmapFromWicBitmap(converter.get(),nullptr,result.put()));
        images.push_front({std::vector<unsigned char>(op.image,op.image+op.imageSize),result});
        size_t retainedBytes{};for(auto const& image:images)retainedBytes+=image.bytes.size();
        while(images.size()>3||retainedBytes>12u*1024*1024)
        {retainedBytes-=images.back().bytes.size();images.pop_back();}
        return result;
    }
    typography::Engine& Typography()
    {
        if(!textFactory)check_hresult(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED,__uuidof(IDWriteFactory),reinterpret_cast<::IUnknown**>(textFactory.put())));
        if(!typographyEngine)typographyEngine=std::make_unique<typography::Engine>(textFactory.get());
        return *typographyEngine;
    }
    static void ValidateV2(GDOpV2 const& operation)
    {
        auto const& op=operation.legacy;
        if(op.kind<0||op.kind>7||!std::isfinite(op.x)||!std::isfinite(op.y)||!std::isfinite(op.width)||!std::isfinite(op.height)
            ||!std::isfinite(op.radius)||!std::isfinite(op.fontSize)||(op.kind!=4&&(op.width<0||op.height<0))
            ||operation.fontWeight<0||operation.fontWeight>999||!std::isfinite(operation.trackingEm)
            ||!std::isfinite(operation.lineHeightDip)||!std::isfinite(operation.baselineDip)
            ||operation.lineHeightDip<0||operation.baselineDip<0||operation.textRole>22||(operation.textFlags&~3u))throw hresult_invalid_argument();
        bool newText=(op.kind==2||op.kind==7)&&operation.textRole!=0;
        if(!newText&&(operation.textRole!=0||operation.fontWeight!=0||operation.trackingEm!=0||operation.lineHeightDip!=0
            ||operation.baselineDip!=0||operation.textFlags!=0||(operation.localeName&&operation.localeName[0])))throw hresult_invalid_argument();
        if(newText&&(op.fontSize<=0||op.fontSize>512))throw hresult_invalid_argument();
        if(op.text&&wcsnlen_s(op.text,65537)>65536)throw hresult_invalid_argument();
        if(operation.localeName&&wcsnlen_s(operation.localeName,LOCALE_NAME_MAX_LENGTH)>=LOCALE_NAME_MAX_LENGTH)throw hresult_invalid_argument();
    }
    typography::Request TextRequest(GDOpV2 const& operation)const
    {
        auto const& op=operation.legacy;typography::Request request;
        request.text=op.text?op.text:L"";request.locale=operation.localeName?operation.localeName:L"";
        request.role=operation.textRole;request.fontWeight=uint32_t(operation.fontWeight);request.flags=operation.textFlags;
        request.fontSize=operation.textRole?op.fontSize:std::max(1.0f,op.fontSize);request.lineHeight=operation.lineHeightDip;
        request.baseline=operation.baselineDip;request.trackingEm=operation.trackingEm;
        request.width=op.width;request.height=op.height;request.dpi=float(dpi);request.bold=op.bold!=0;request.icon=op.kind==6;
        request.wrap=op.kind==7;request.center=op.center!=0;request.trim=op.kind!=6;request.paragraphCenter=true;return request;
    }
    void Paint(ID2D1DeviceContext* context,GDOp const* operations,unsigned count,ID2D1SolidColorBrush* sharedBrush=nullptr)
    {
        com_ptr<ID2D1SolidColorBrush> brush;
        if(sharedBrush)brush.copy_from(sharedBrush);else check_hresult(context->CreateSolidColorBrush(D2D1::ColorF(0,0,0,1),brush.put()));
        for(unsigned index=0;index<count;++index)
        {
            auto const& op=operations[index];auto rect=D2D1::RectF(op.x,op.y,op.x+op.width,op.y+op.height);
            brush->SetColor(Color(op.color));
            switch(op.kind)
            {
            case 0:context->FillRectangle(rect,brush.get());break;
            case 1:context->FillRoundedRectangle({rect,std::max(0.0f,op.radius),std::max(0.0f,op.radius)},brush.get());break;
            case 2: case 6: case 7:
                if(op.text&&op.width>0&&op.height>0)
                {
                    com_ptr<IDWriteTextFormat> format;check_hresult(textFactory->CreateTextFormat(op.kind==6?L"Segoe Fluent Icons":L"Segoe UI Variable Text",nullptr,op.bold?DWRITE_FONT_WEIGHT_SEMI_BOLD:DWRITE_FONT_WEIGHT_NORMAL,DWRITE_FONT_STYLE_NORMAL,DWRITE_FONT_STRETCH_NORMAL,std::max(1.0f,op.fontSize),L"",format.put()));
                    check_hresult(format->SetWordWrapping(op.kind==7?DWRITE_WORD_WRAPPING_WRAP:DWRITE_WORD_WRAPPING_NO_WRAP));
                    check_hresult(format->SetTextAlignment(op.center?DWRITE_TEXT_ALIGNMENT_CENTER:DWRITE_TEXT_ALIGNMENT_LEADING));
                    check_hresult(format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER));
                    std::wstring text(op.text);
                    if(op.kind!=7)
                        for(auto& character:text)if(character==L'\r'||character==L'\n'||character==L'\t')character=L' ';
                    com_ptr<IDWriteTextLayout> layout;
                    check_hresult(textFactory->CreateTextLayout(text.c_str(),static_cast<UINT32>(text.size()),format.get(),op.width,op.height,layout.put()));
                    if(text.size()==5&&text[2]==L':'&&text[0]>=L'0'&&text[0]<=L'9'&&text[1]>=L'0'&&text[1]<=L'9'&&text[3]>=L'0'&&text[3]<=L'9'&&text[4]>=L'0'&&text[4]<=L'9')
                    {
                        com_ptr<IDWriteTypography> typography;check_hresult(textFactory->CreateTypography(typography.put()));
                        check_hresult(typography->AddFontFeature({DWRITE_FONT_FEATURE_TAG_TABULAR_FIGURES,1}));
                        check_hresult(layout->SetTypography(typography.get(),{0,static_cast<UINT32>(text.size())}));
                    }
                    if(op.kind!=6)
                    {
                        com_ptr<IDWriteInlineObject> ellipsis;check_hresult(textFactory->CreateEllipsisTrimmingSign(format.get(),ellipsis.put()));
                        DWRITE_TRIMMING trimming{DWRITE_TRIMMING_GRANULARITY_CHARACTER,0,0};check_hresult(layout->SetTrimming(&trimming,ellipsis.get()));
                    }
                    context->DrawTextLayout({op.x,op.y},layout.get(),brush.get(),D2D1_DRAW_TEXT_OPTIONS_CLIP);
                }
                break;
            case 3:
                try
                {
                    auto bitmap=Decode(op);
                    if(bitmap&&op.width>0&&op.height>0)
                    {
                        // Cover rather than stretch: preserve the artwork's
                        // proportions for portrait and landscape thumbnails.
                        auto size=bitmap->GetSize();auto source=D2D1::RectF(0,0,size.width,size.height);
                        auto aspect=op.width/op.height;
                        if(size.width>size.height*aspect)
                        {auto cropWidth=size.height*aspect;source.left=(size.width-cropWidth)/2;source.right=source.left+cropWidth;}
                        else
                        {auto cropHeight=size.width/aspect;source.top=(size.height-cropHeight)/2;source.bottom=source.top+cropHeight;}
                        if(op.radius>0)
                        {
                            com_ptr<ID2D1RoundedRectangleGeometry> mask;
                            float radius=std::min(op.radius,std::min(op.width,op.height)/2);
                            check_hresult(d2dFactory->CreateRoundedRectangleGeometry({rect,radius,radius},mask.put()));
                            context->PushLayer(D2D1::LayerParameters1(rect,mask.get()),nullptr);
                            context->DrawBitmap(bitmap.get(),rect,1,D2D1_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC,&source);
                            context->PopLayer();
                        }
                        else context->DrawBitmap(bitmap.get(),rect,1,D2D1_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC,&source);
                    }
                }
                catch(...){/* An unreadable artwork does not invalidate the text surface. */}
                break;
            case 4:context->DrawLine({op.x,op.y},{op.x+op.width,op.y+op.height},brush.get(),std::max(.5f,op.radius));break;
            case 5:context->FillEllipse({{op.x+op.width/2,op.y+op.height/2},op.width/2,op.height/2},brush.get());break;
            default:throw hresult_invalid_argument();
            }
        }
    }
    bool DeviceLost(HRESULT error)const
    {return error==D2DERR_RECREATE_TARGET||error==DXGI_ERROR_DEVICE_REMOVED||error==DXGI_ERROR_DEVICE_RESET||(d3d&&FAILED(d3d->GetDeviceRemovedReason()));}
    void PaintV2(ID2D1DeviceContext* context,GDOpV2 const* operations,unsigned count)
    {
        com_ptr<ID2D1SolidColorBrush> brush;check_hresult(context->CreateSolidColorBrush(D2D1::ColorF(0,0,0,1),brush.put()));
        for(unsigned index=0;index<count;++index)
        {
            auto const& extension=operations[index];auto const& op=extension.legacy;
            if(extension.textRole&&(op.kind==2||op.kind==7))
            {
                if(op.text&&op.width>0&&op.height>0)
                {
                    auto layout=Typography().CreateLayout(TextRequest(extension));brush->SetColor(Color(op.color));
                    context->DrawTextLayout({op.x,op.y},layout->textLayout.get(),brush.get(),D2D1_DRAW_TEXT_OPTIONS(D2D1_DRAW_TEXT_OPTIONS_CLIP|D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT));
                }
            }
            else Paint(context,&op,1,brush.get()); // Exact legacy/icon/non-text branch.
        }
    }
    CompositionDrawingSurface Render(float width,float height,GDOp const* operations,unsigned count)
    {
        if(!std::isfinite(width)||!std::isfinite(height)||width<=0||height<=0||width>4096||height>4096||count>4096||(!operations&&count))throw hresult_invalid_argument();
        for(unsigned index=0;index<count;++index)
        {
            auto const& op=operations[index];
            if(!std::isfinite(op.x)||!std::isfinite(op.y)||!std::isfinite(op.width)||!std::isfinite(op.height)||!std::isfinite(op.radius)||!std::isfinite(op.fontSize)||(op.kind!=4&&(op.width<0||op.height<0)))throw hresult_invalid_argument();
        }
        for(int attempt=0;attempt<2;++attempt)
        {
            try
            {
                if(!graphics){CreateGraphics();CreateGrain();}
                auto surface=CreateSurface(int(std::ceil(width*Scale())),int(std::ceil(height*Scale())));
                Draw(surface,float(dpi),[&](auto context){Paint(context,operations,count);});return surface;
            }
            catch(hresult_error const& error)
            {
                if(attempt||!DeviceLost(error.code()))throw;
                fallback=true;ApplyMaterial();
                CreateGraphics();CreateGrain();fallback=false;ApplyMaterial();
                PostMessageW(window,RecoveredMessage,0,0); // C# recreates its content after this call has returned.
            }
        }
        throw hresult_error(E_FAIL);
    }
    CompositionDrawingSurface RenderV2(float width,float height,GDOpV2 const* operations,unsigned count)
    {
        if(!std::isfinite(width)||!std::isfinite(height)||width<=0||height<=0||width>4096||height>4096||count>4096||(!operations&&count))throw hresult_invalid_argument();
        for(unsigned index=0;index<count;++index)ValidateV2(operations[index]);
        for(int attempt=0;attempt<2;++attempt)
        {
            try
            {
                if(!graphics){CreateGraphics();CreateGrain();}
                auto surface=CreateSurface(int(std::ceil(width*Scale())),int(std::ceil(height*Scale())));
                Draw(surface,float(dpi),[&](auto context){PaintV2(context,operations,count);});return surface;
            }
            catch(hresult_error const& error)
            {
                if(attempt||!DeviceLost(error.code()))throw;
                fallback=true;ApplyMaterial();CreateGraphics();CreateGrain();fallback=false;ApplyMaterial();
                PostMessageW(window,RecoveredMessage,0,0);
            }
        }
        throw hresult_error(E_FAIL);
    }
    void Shutdown()noexcept
    {
        if(destroying)return;destroying=true;callback=nullptr;
        if(window)KillTimer(window,DpiTimer);
        if(window)KillTimer(window,LiquidTimer);
        liquid.reset();liquidSurface=nullptr;liquidBrush=nullptr;
        if(captured&&GetCapture()==window)ReleaseCapture();captured=false;
        {std::lock_guard lock(accessibility->mutex);accessibility->hwnd=nullptr;accessibility->root=nullptr;accessibility->visible=false;accessibility->items.clear();}
        if(provider)UiaDisconnectProvider(provider.Get());provider.Reset();
        // Invalidate the HWND under the same lock used by UIA posting before
        // draining requests; no other COM thread can post another allocation.
        MSG message{};
        if(window)while(PeekMessageW(&message,window,RangeMessage,RangeMessage,PM_REMOVE))delete reinterpret_cast<RangeRequest*>(message.lParam);
        if(IsWindow(shadow))DestroyWindow(shadow);shadow=nullptr;ReleaseShadowBitmap();
        if(IsWindow(window))DestroyWindow(window);window=nullptr;
        try{if(target)target.Root(nullptr);}catch(...){}
        try{if(root)root.Children().RemoveAll();}catch(...){}
        images.clear();grainSurface=nullptr;graphics=nullptr;
        content=nullptr;glass=nullptr;tint=nullptr;grain=nullptr;border=nullptr;borderGeometry=nullptr;borderShape=nullptr;clipGeometry=nullptr;
        tintBrush=nullptr;borderBrush=nullptr;solidTintBrush=nullptr;solidBorderBrush=nullptr;
        backdrop=nullptr;root=nullptr;target=nullptr;
        try{if(compositor)compositor.Close();}catch(...){}
        compositor=nullptr;
        typographyEngine.reset();cacheContext=nullptr;wic=nullptr;textFactory=nullptr;d2d=nullptr;d2dFactory=nullptr;geometryFactory=nullptr;d3dContext=nullptr;d3d=nullptr;
        try
        {
            if(queue)
            {
                auto shutdown=queue.ShutdownQueueAsync();auto deadline=GetTickCount64()+2000;
                while(shutdown.Status()==winrt::Windows::Foundation::AsyncStatus::Started&&GetTickCount64()<deadline)
                {
                    while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessageW(&message);}
                    MsgWaitForMultipleObjectsEx(0,nullptr,10,QS_ALLINPUT,MWMO_INPUTAVAILABLE);
                }
            }
        }catch(...){}
        queue=nullptr;
    }
};
using HostRef=std::shared_ptr<Host>;
HostRef FromContext(void* context){return context?*static_cast<HostRef*>(context):HostRef{};}

LRESULT CALLBACK ShadowProc(HWND hwnd,UINT message,WPARAM wp,LPARAM lp)
{
    if(message==WM_NCCREATE)
        SetWindowLongPtrW(hwnd,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(reinterpret_cast<CREATESTRUCTW*>(lp)->lpCreateParams));
    if(message==WM_DESTROY)
    {
        auto host=reinterpret_cast<Host*>(GetWindowLongPtrW(hwnd,GWLP_USERDATA));
        if(host&&host->liquid)host->liquid->Suspend(true,true);
    }
    if(message==WM_NCDESTROY)SetWindowLongPtrW(hwnd,GWLP_USERDATA,0);
    if(message==WM_NCHITTEST)return HTTRANSPARENT;
    if(message==WM_MOUSEACTIVATE)return MA_NOACTIVATE;
    if(message==WM_ERASEBKGND)return 1;
    return DefWindowProcW(hwnd,message,wp,lp);
}
LRESULT CALLBACK WindowProc(HWND hwnd,UINT message,WPARAM wp,LPARAM lp)
{
    auto host=reinterpret_cast<Host*>(GetWindowLongPtrW(hwnd,GWLP_USERDATA));
    if(message==WM_NCCREATE)
    {host=static_cast<Host*>(reinterpret_cast<CREATESTRUCTW*>(lp)->lpCreateParams);host->window=hwnd;SetWindowLongPtrW(hwnd,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(host));}
    if(!host)return DefWindowProcW(hwnd,message,wp,lp);
    // Managed callbacks may synchronously quit and release the external host
    // handle. Keep this dispatch alive until the callback and WndProc return.
    auto dispatchLifetime=host->shared_from_this();
    try
    {
        switch(message)
        {
        case WM_MOUSEACTIVATE:return MA_NOACTIVATE;
        case WM_ERASEBKGND:return 1;
        case WM_SIZE:host->Layout();host->UpdateAccessibleGeometry();host->Emit(ContextEvent);return 0;
        case WM_WINDOWPOSCHANGED:host->UpdateShadow();host->UpdateLiquid();host->UpdateAccessibleGeometry();host->Emit(ContextEvent);break;
        case WM_DPICHANGED:
        {auto rectangle=reinterpret_cast<RECT*>(lp);host->ApplyDpi(LOWORD(wp),rectangle->left,rectangle->top);return 0;}
        case WM_SETTINGCHANGE:
            host->ReconcileDpi();SetTimer(hwnd,DpiTimer,300,nullptr);host->ApplyMaterial();host->Emit(12);break;
        case WM_TIMECHANGE:host->Emit(12);return 0;
        case WM_DISPLAYCHANGE:host->ReconcileDpi();SetTimer(hwnd,DpiTimer,300,nullptr);host->Emit(10);break;
        case WM_TIMER:
            if(wp==DpiTimer){KillTimer(hwnd,DpiTimer);host->ReconcileDpi();return 0;}
            if(wp==LiquidTimer){host->UpdateLiquid();return 0;}break;
        case WM_POWERBROADCAST:if(wp==PBT_APMRESUMEAUTOMATIC||wp==PBT_APMRESUMESUSPEND){host->ReconcileDpi();host->Emit(11);}break;
        case WM_MOUSEMOVE:
        {
            if(!host->visible)return 0;
            bool inside=host->Inside(GET_X_LPARAM(lp)/host->Scale(),GET_Y_LPARAM(lp)/host->Scale());
            if(inside&&!host->tracking){host->tracking=true;TRACKMOUSEEVENT track{sizeof(track),TME_LEAVE,hwnd,0};TrackMouseEvent(&track);host->EmitPointer(1,lp);}
            else if(!inside&&host->tracking){host->tracking=false;host->EmitPointer(2,lp);}
            host->EmitPointer(3,lp);return 0;
        }
        case WM_MOUSELEAVE:if(host->tracking){host->tracking=false;host->Emit(2);}return 0;
        case WM_LBUTTONDOWN:if(!host->visible)return 0;host->captured=true;SetCapture(hwnd);host->EmitPointer(4,lp);return 0;
        case WM_LBUTTONUP:if(!host->visible)return 0;host->EmitPointer(5,lp);host->captured=false;if(GetCapture()==hwnd)ReleaseCapture();return 0;
        case WM_CAPTURECHANGED:if(host->captured){host->captured=false;host->Emit(15);}return 0;
        case WM_MOUSEWHEEL:
        {if(!host->visible)return 0;POINT point{GET_X_LPARAM(lp),GET_Y_LPARAM(lp)};ScreenToClient(hwnd,&point);host->Emit(6,point.x/host->Scale(),point.y/host->Scale(),GET_X_LPARAM(lp),GET_Y_LPARAM(lp),host->Hit(point.x/host->Scale(),point.y/host->Scale()),double(GET_WHEEL_DELTA_WPARAM(wp))/WHEEL_DELTA);return 0;}
        case WM_SETCURSOR:if(LOWORD(lp)==HTCLIENT){SetCursor(LoadCursor(nullptr,IDC_ARROW));return TRUE;}break;
        case WM_NCHITTEST:
        {
            POINT point{GET_X_LPARAM(lp),GET_Y_LPARAM(lp)};ScreenToClient(hwnd,&point);
            return host->Inside(point.x/host->Scale(),point.y/host->Scale())?HTCLIENT:HTTRANSPARENT;
        }
        case WM_GETOBJECT:
            if(static_cast<LONG>(lp)==UiaRootObjectId&&host->provider)return UiaReturnRawElementProvider(hwnd,wp,lp,host->provider.Get());break;
        case InvokeMessage:
        {
            bool enabled=false;{std::lock_guard lock(host->accessibility->mutex);for(auto const& item:host->accessibility->items)if(item.id==static_cast<int>(wp))enabled=item.enabled;}
            if(enabled)host->Emit(7,0,0,0,0,static_cast<int>(wp));return 0;
        }
        case RangeMessage:
        {
            auto request=std::unique_ptr<RangeRequest>(reinterpret_cast<RangeRequest*>(lp));if(!request)return 0;
            bool enabled=false;{std::lock_guard lock(host->accessibility->mutex);for(auto const& item:host->accessibility->items)if(item.id==request->id){enabled=item.enabled&&item.range;request->value=std::clamp(request->value,item.minimum,item.maximum);}}
            if(enabled)host->Emit(8,0,0,0,0,request->id,request->value);return 0;
        }
        case RecoveredMessage:host->Emit(12,0,0,0,0,-1);return 0;
        case WM_CLOSE:host->Emit(14);return 0;
        case WM_PAINT:{PAINTSTRUCT paint{};BeginPaint(hwnd,&paint);EndPaint(hwnd,&paint);return 0;}
        case WM_DESTROY:
            // Also cover an external DestroyWindow path: join the QA affinity
            // watchdog while the HWNDs are still valid, before handle reuse.
            if(host->liquid)host->liquid->Suspend(true,true);
            KillTimer(hwnd,DpiTimer);KillTimer(hwnd,LiquidTimer);return 0;
        case WM_NCDESTROY:SetWindowLongPtrW(hwnd,GWLP_USERDATA,0);break;
        }
    }
    catch(...)
    {
        auto error=to_hresult();host->fallback=true;
        try{host->ApplyMaterial();}catch(...){}
        host->Emit(12,0,0,0,0,-2,static_cast<unsigned>(error));
    }
    return DefWindowProcW(hwnd,message,wp,lp);
}
void RegisterWindows()
{
    WNDCLASSEXW cls{sizeof(cls)};cls.hInstance=GetModuleHandleW(nullptr);cls.hCursor=LoadCursor(nullptr,IDC_ARROW);
    cls.lpfnWndProc=WindowProc;cls.lpszClassName=WindowClass;
    if(!RegisterClassExW(&cls)&&GetLastError()!=ERROR_CLASS_ALREADY_EXISTS)throw hresult_error(HRESULT_FROM_WIN32(GetLastError()));
    cls.lpfnWndProc=ShadowProc;cls.lpszClassName=ShadowClass;
    if(!RegisterClassExW(&cls)&&GetLastError()!=ERROR_CLASS_ALREADY_EXISTS)throw hresult_error(HRESULT_FROM_WIN32(GetLastError()));
}
}

extern "C" __declspec(dllexport) HRESULT __stdcall GDCreate(GDCallback callback,void* user,void** context,HWND* hwnd)
{
    if(!context||!hwnd)return E_POINTER;*context=nullptr;*hwnd=nullptr;
    try
    {
        auto host=std::make_shared<gd::Host>();
        if(!winrt::Windows::System::DispatcherQueue::GetForCurrentThread())
        {
            DispatcherQueueOptions options{sizeof(options),DQTYPE_THREAD_CURRENT,DQTAT_COM_NONE};
            check_hresult(CreateDispatcherQueueController(options,reinterpret_cast<ABI::Windows::System::IDispatcherQueueController**>(put_abi(host->queue))));
        }
        gd::RegisterWindows();
        auto window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TOPMOST|WS_EX_NOREDIRECTIONBITMAP,gd::WindowClass,
            L"GlassDesk 音乐小组件",WS_POPUP,0,0,232,48,nullptr,nullptr,GetModuleHandleW(nullptr),host.get());
        check_bool(window!=nullptr);host->Initialize(window);
        host->callback=callback;host->user=user;*hwnd=window;*context=new gd::HostRef(host);return S_OK;
    }catch(...){return to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDDestroy(void* context)
{
    auto host=gd::FromContext(context);if(!host)return S_OK;
    auto hr=host->CheckThread();if(FAILED(hr))return hr;host->Shutdown();delete static_cast<gd::HostRef*>(context);return S_OK;
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDGetContext(void* context,GDContext* result)
{
    auto host=gd::FromContext(context);if(!host||!result)return E_POINTER;
    auto hr=host->CheckThread();if(FAILED(hr))return hr;*result=host->Context();return S_OK;
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDGetComposition(void* context,::IInspectable** compositor,::IInspectable** content)
{
    if(!context||!compositor||!content)return E_POINTER;*compositor=nullptr;*content=nullptr;
    auto host=gd::FromContext(context);auto hr=host->CheckThread();if(FAILED(hr))return hr;
    auto compositorCopy=host->compositor;auto contentCopy=host->content;
    *compositor=static_cast<::IInspectable*>(detach_abi(compositorCopy));*content=static_cast<::IInspectable*>(detach_abi(contentCopy));return S_OK;
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDCreateCompositionPath(void* context,GDPoint const* points,unsigned count,float rounding,::IInspectable** result)
{
    auto host=gd::FromContext(context);if(!host||!points||!result)return E_POINTER;*result=nullptr;
    auto hr=host->CheckThread();if(FAILED(hr))return hr;
    if(count<3||count>64||!std::isfinite(rounding)||rounding<0||rounding>100)return E_INVALIDARG;
    for(unsigned i=0;i<count;++i)if(!std::isfinite(points[i].x)||!std::isfinite(points[i].y)||std::abs(points[i].x)>4096||std::abs(points[i].y)>4096)return E_INVALIDARG;
    try
    {
        if(!host->geometryFactory)check_hresult(D2D1CreateFactory(D2D1_FACTORY_TYPE_MULTI_THREADED,host->geometryFactory.put()));
        auto source=make<gd::GeometrySource>(host->geometryFactory.get(),std::vector<GDPoint>(points,points+count),rounding);
        CompositionPath path(source);*result=static_cast<::IInspectable*>(detach_abi(path));return S_OK;
    }catch(...){return to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDApplyFrame(void* context,GDFrame const* frame)
{
    auto host=gd::FromContext(context);if(!host||!frame)return E_POINTER;
    auto hr=host->CheckThread();if(FAILED(hr))return hr;
    if(!std::isfinite(frame->width)||!std::isfinite(frame->height)||!std::isfinite(frame->radius)||!std::isfinite(frame->blur)||!std::isfinite(frame->tint)||frame->width<1||frame->height<1||frame->width>4096||frame->height>4096||frame->radius<0||frame->blur<0||frame->blur>100)return E_INVALIDARG;
    try
    {
        host->frame=*frame;
        SetWindowPos(host->window,nullptr,0,0,int(std::lround(frame->width*host->Scale())),int(std::lround(frame->height*host->Scale())),SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE);
        host->Layout();return S_OK;
    }catch(...){return to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDSetPlacement(void* context,int x,int y)
{
    auto host=gd::FromContext(context);if(!host)return E_POINTER;
    auto hr=host->CheckThread();if(FAILED(hr))return hr;
    return SetWindowPos(host->window,nullptr,x,y,0,0,SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE)?S_OK:HRESULT_FROM_WIN32(GetLastError());
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDSetVisible(void* context,int visible)
{
    auto host=gd::FromContext(context);if(!host)return E_POINTER;auto hr=host->CheckThread();if(FAILED(hr))return hr;
    try
    {
        host->visible=visible!=0;
        host->UpdateLiquid();
        if(!host->visible)
        {
            TRACKMOUSEEVENT cancel{sizeof(cancel),TME_CANCEL|TME_LEAVE,host->window,0};TrackMouseEvent(&cancel);
            host->tracking=false;
            bool wasCaptured=host->captured;host->captured=false;
            if(GetCapture()==host->window)ReleaseCapture();
            if(wasCaptured)host->Emit(15);
        }
        ShowWindow(host->window,visible?SW_SHOWNOACTIVATE:SW_HIDE);host->UpdateShadow();host->UpdateAccessibleGeometry();
        return S_OK;
    }catch(...){return to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDSetLiquidGlassEnabled(void* context,int enabled)
{
    auto host=gd::FromContext(context);if(!host)return E_POINTER;
    auto hr=host->CheckThread();if(FAILED(hr))return hr;
    try{host->liquidEnabled=enabled!=0;host->UpdateLiquid();return S_OK;}catch(...){return to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDGetLiquidGlassStatus(void* context,wchar_t* text,unsigned capacity)
{
    auto host=gd::FromContext(context);if(!host||!text)return E_POINTER;
    auto hr=host->CheckThread();if(FAILED(hr))return hr;if(!capacity)return E_INVALIDARG;
    try
    {
        auto status=host->liquid?host->liquid->Status():host->liquidEnabled?L"Fallback: D3D material device unavailable; enabled intent retained":L"Disabled";
        if(status.size()+1>capacity)return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
        wcscpy_s(text,capacity,status.c_str());return S_OK;
    }catch(...){return to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDSaveLiquidGlassDiagnosticFrame(void* context,wchar_t const* path)
{
    auto host=gd::FromContext(context);if(!host)return E_POINTER;
    auto hr=host->CheckThread();if(FAILED(hr))return hr;
    return host->liquid?host->liquid->SaveDiagnostic(path):HRESULT_FROM_WIN32(ERROR_NOT_READY);
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDBeginDiagnosticSnapshot(void* context,unsigned durationMilliseconds)
{
    auto host=gd::FromContext(context);if(!host)return E_POINTER;
    auto hr=host->CheckThread();if(FAILED(hr))return hr;
    if(!host->visible||!host->liquidEnabled||host->frame.solid||host->fallback||!host->liquid)
        return HRESULT_FROM_WIN32(ERROR_NOT_READY);
    return host->liquid->BeginDiagnosticSnapshot(durationMilliseconds);
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDEndDiagnosticSnapshot(void* context)
{
    auto host=gd::FromContext(context);if(!host)return E_POINTER;
    auto hr=host->CheckThread();if(FAILED(hr))return hr;
    return host->liquid?host->liquid->EndDiagnosticSnapshot():S_FALSE;
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDGetDiagnosticSnapshotStatus(void* context,wchar_t* text,unsigned capacity)
{
    auto host=gd::FromContext(context);if(!host||!text)return E_POINTER;
    auto hr=host->CheckThread();if(FAILED(hr))return hr;if(!capacity)return E_INVALIDARG;
    try
    {
        auto status=host->liquid?host->liquid->DiagnosticSnapshotStatus():L"QA final-UI snapshot: inactive; renderer unavailable";
        if(status.size()+1>capacity)return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
        wcscpy_s(text,capacity,status.c_str());return S_OK;
    }catch(...){return to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDSetAccessible(void* context,wchar_t const* name,GDAccessible const* items,unsigned count)
{
    auto host=gd::FromContext(context);if(!host||(!items&&count))return E_POINTER;auto hr=host->CheckThread();if(FAILED(hr))return hr;
    if(count>128)return E_INVALIDARG;
    try
    {
        std::vector<gd::AccessibleItem> values;values.reserve(count);
        for(unsigned index=0;index<count;++index)
        {
            auto const& item=items[index];
            if(!std::isfinite(item.x)||!std::isfinite(item.y)||!std::isfinite(item.width)||!std::isfinite(item.height)||item.width<0||item.height<0||!std::isfinite(item.value)||!std::isfinite(item.minimum)||!std::isfinite(item.maximum)||item.maximum<item.minimum)return E_INVALIDARG;
            if(std::any_of(values.begin(),values.end(),[&](auto const& value){return value.id==item.id;}))return E_INVALIDARG;
            values.push_back({item.id,item.name?item.name:L"",item.x,item.y,item.width,item.height,item.enabled!=0,item.isRange!=0,std::clamp(item.value,item.minimum,item.maximum),item.minimum,item.maximum});
        }
        {std::lock_guard lock(host->accessibility->mutex);host->accessibility->name=name?name:L"GlassDesk 音乐小组件";host->accessibility->items=std::move(values);}
        if(UiaClientsAreListening())UiaRaiseStructureChangedEvent(host->provider.Get(),StructureChangeType_ChildrenInvalidated,nullptr,0);
        return S_OK;
    }catch(...){return to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDRenderSurface(void* context,float width,float height,GDOp const* operations,unsigned count,::IInspectable** surface)
{
    if(!context||!surface)return E_POINTER;*surface=nullptr;auto host=gd::FromContext(context);auto hr=host->CheckThread();if(FAILED(hr))return hr;
    try{auto value=host->Render(width,height,operations,count);*surface=static_cast<::IInspectable*>(detach_abi(value));return S_OK;}catch(...){return to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDGetNativeAbiInfo(GDNativeAbiInfo* result)
{
    if(!result)return E_POINTER;if(result->size!=sizeof(GDNativeAbiInfo))return E_INVALIDARG;
    *result={sizeof(GDNativeAbiInfo),2,sizeof(GDOp),sizeof(GDOpV2),1u|2u|4u|8u,0};return S_OK;
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDConfigureTypographyV2(void* context,uint32_t profile,uint32_t tokenRevision,wchar_t const* qaFontPath)
{
    auto host=gd::FromContext(context);if(!host)return E_POINTER;auto hr=host->CheckThread();if(FAILED(hr))return hr;
    if(profile>2||tokenRevision!=1)return E_INVALIDARG;
    if(profile!=2&&qaFontPath&&qaFontPath[0])return E_INVALIDARG;
    if(qaFontPath&&wcsnlen_s(qaFontPath,32768)>=32768)return E_INVALIDARG;
    try{host->Typography().Configure(gd::typography::Profile(profile),tokenRevision,qaFontPath?qaFontPath:L"");return S_OK;}catch(...){return to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDGetTypographyCacheStats(void* context,GDTypographyCacheStats* result)
{
    auto host=gd::FromContext(context);if(!host||!result)return E_POINTER;*result={};auto hr=host->CheckThread();if(FAILED(hr))return hr;
    try
    {
        if(host->typographyEngine){auto stats=host->typographyEngine->Stats();*result={uint64_t(stats.formats),uint64_t(stats.layouts),uint64_t(stats.formatHits),uint64_t(stats.layoutHits),uint64_t(stats.layoutsCreated)};}
        return S_OK;
    }catch(...){return to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDMeasureTextV2(void* context,GDOpV2 const* operation,uint32_t stride,GDTextMetrics* result)
{
    if(!result)return E_POINTER;*result={};auto host=gd::FromContext(context);if(!host||!operation)return E_POINTER;
    auto hr=host->CheckThread();if(FAILED(hr))return hr;if(stride!=sizeof(GDOpV2))return E_INVALIDARG;
    try
    {
        host->ValidateV2(*operation);auto const& op=operation->legacy;
        if((op.kind!=2&&op.kind!=6&&op.kind!=7)||!op.text||op.width<=0||op.height<=0)return E_INVALIDARG;
        auto layout=host->Typography().CreateLayout(host->TextRequest(*operation));auto const& metrics=layout->metrics;
        *result={metrics.advance,metrics.widthIncludingTrailingWhitespace,metrics.height,metrics.baseline,metrics.lineHeight,metrics.lineCount};return S_OK;
    }catch(...){return to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT __stdcall GDRenderSurfaceV2(void* context,float width,float height,GDOpV2 const* operations,uint32_t count,uint32_t stride,::IInspectable** surface)
{
    if(!surface)return E_POINTER;*surface=nullptr;auto host=gd::FromContext(context);if(!host)return E_POINTER;
    auto hr=host->CheckThread();if(FAILED(hr))return hr;if(stride!=sizeof(GDOpV2))return E_INVALIDARG;
    try{auto value=host->RenderV2(width,height,operations,count);*surface=static_cast<::IInspectable*>(detach_abi(value));return S_OK;}catch(...){return to_hresult();}
}
