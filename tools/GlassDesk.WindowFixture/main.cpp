#include <windows.h>
#include <filesystem>
#include <fstream>
#include <string_view>

// Independent GUI fixture: exercises actual foreground and borderless window changes.
// It never changes system settings or sends input to another process.
namespace {
bool fullscreen=false;
bool backdropOnly=false;
int palette=0,clicks=0,rightClicks=0,middleClicks=0,wheels=0,menuCommands=0;
RECT restore{};
std::ofstream events;
void Record(const char* event,HWND window) {
    RECT bounds{};GetWindowRect(window,&bounds);
    events << "{\"event\":\"" << event << "\",\"fullscreen\":" << (fullscreen?"true":"false") << ",\"palette\":" << palette << ",\"clicks\":" << clicks
           << ",\"rightClicks\":" << rightClicks << ",\"middleClicks\":" << middleClicks << ",\"wheels\":" << wheels << ",\"menuCommands\":" << menuCommands
           << ",\"hwnd\":" << reinterpret_cast<ULONG_PTR>(window)
           << ",\"foreground\":" << reinterpret_cast<ULONG_PTR>(GetForegroundWindow())
           << ",\"x\":" << bounds.left << ",\"y\":" << bounds.top
           << ",\"width\":" << bounds.right-bounds.left << ",\"height\":" << bounds.bottom-bounds.top << "}" << std::endl;
}
void ToggleFullscreen(HWND window) {
    fullscreen=!fullscreen;
    if(fullscreen) {
        GetWindowRect(window,&restore);
        MONITORINFO monitor{sizeof(monitor),{}, {}, 0};
        GetMonitorInfoW(MonitorFromWindow(window,MONITOR_DEFAULTTONEAREST),&monitor);
        SetWindowLongPtrW(window,GWL_STYLE,WS_POPUP|WS_VISIBLE);
        SetWindowPos(window,nullptr,monitor.rcMonitor.left,monitor.rcMonitor.top,
            monitor.rcMonitor.right-monitor.rcMonitor.left,monitor.rcMonitor.bottom-monitor.rcMonitor.top,
            SWP_NOZORDER|SWP_NOACTIVATE|SWP_FRAMECHANGED);
    } else {
        SetWindowLongPtrW(window,GWL_STYLE,WS_OVERLAPPEDWINDOW|WS_VISIBLE);
        SetWindowPos(window,nullptr,restore.left,restore.top,restore.right-restore.left,restore.bottom-restore.top,
            SWP_NOZORDER|SWP_NOACTIVATE|SWP_FRAMECHANGED);
    }
    InvalidateRect(window,nullptr,TRUE);Record("mode",window);
}
LRESULT CALLBACK WindowProc(HWND window,UINT message,WPARAM word,LPARAM value) {
    switch(message) {
    case WM_MOUSEACTIVATE: if(backdropOnly)return MA_NOACTIVATE;break;
    case WM_KEYDOWN:
        if(word==VK_F11) {ToggleFullscreen(window);return 0;}
        if(word==VK_ESCAPE&&fullscreen) {ToggleFullscreen(window);return 0;}
        if(word==VK_F5) {palette=(palette+1)%4;InvalidateRect(window,nullptr,TRUE);Record("palette",window);return 0;}
        break;
    case WM_LBUTTONDOWN: Record("pointer-down",window);break;
    case WM_LBUTTONUP: ++clicks;Record("pointer-up",window);break;
    case WM_RBUTTONDOWN: Record("right-down",window);break;
    case WM_RBUTTONUP: ++rightClicks;Record("right-up",window);break;
    case WM_MBUTTONDOWN: Record("middle-down",window);break;
    case WM_MBUTTONUP: ++middleClicks;Record("middle-up",window);break;
    case WM_MOUSEWHEEL: ++wheels;Record("wheel",window);break;
    case WM_CONTEXTMENU: {
        HMENU menu=CreatePopupMenu();if(!menu)return 0;
        AppendMenuW(menu,MF_STRING,101,L"QA click arrived once");
        POINT point{};GetCursorPos(&point);Record("context-menu-open",window);
        auto command=TrackPopupMenu(menu,TPM_RETURNCMD|TPM_NONOTIFY|TPM_RIGHTBUTTON,point.x,point.y,0,window,nullptr);
        DestroyMenu(menu);if(command){++menuCommands;Record("context-menu-command",window);}return 0;
    }
    case WM_SETFOCUS: Record("focus",window);break;
    case WM_PAINT: {
        PAINTSTRUCT paint{};HDC dc=BeginPaint(window,&paint);RECT area{};GetClientRect(window,&area);
        HBRUSH brush=CreateSolidBrush(palette==1?RGB(247,248,249):palette==3?RGB(128,132,138):RGB(30,39,44));FillRect(dc,&area,brush);DeleteObject(brush);
        if(palette==2)
        {
            COLORREF colors[]{RGB(39,131,143),RGB(181,69,105),RGB(71,101,61),RGB(199,165,88)};
            for(int y=0;y<area.bottom;y+=56)for(int x=0;x<area.right;x+=56)
            {
                RECT tile{x,y,x+56,y+56};auto tileBrush=CreateSolidBrush(colors[(x/56+y/56)%4]);
                FillRect(dc,&tile,tileBrush);DeleteObject(tileBrush);
            }
        }
        SetTextColor(dc,palette==1?RGB(35,42,48):RGB(242,246,255));SetBkMode(dc,TRANSPARENT);
        DrawTextW(dc,fullscreen?L"GlassDesk QA: borderless fullscreen. F11 or Esc returns to normal."
            :L"GlassDesk QA: ordinary captioned window. F11 toggles borderless fullscreen.",-1,&area,
            DT_CENTER|DT_VCENTER|DT_SINGLELINE);
        EndPaint(window,&paint);return 0;
    }
    case WM_CLOSE: Record("close",window);DestroyWindow(window);return 0;
    case WM_DESTROY: PostQuitMessage(0);return 0;
    }
    return DefWindowProcW(window,message,word,value);
}
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,PWSTR arguments,int) {
    if(std::wstring_view(arguments?arguments:L"").find(L"--quit-existing")!=std::wstring_view::npos) {
        // The fixture's explicit lifecycle command, also usable while minimized.
        // It targets only this development fixture's unique native class.
        HWND existing=FindWindowW(L"GlassDesk.QA.WindowFixture",L"GlassDesk QA Window Fixture");
        if(!existing)return 0;
        if(!PostMessageW(existing,WM_CLOSE,0,0))return 3;
        for(int attempt=0;attempt<100&&IsWindow(existing);++attempt)Sleep(20);
        return IsWindow(existing)?3:0;
    }
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    wchar_t module[MAX_PATH]{};GetModuleFileNameW(nullptr,module,MAX_PATH);
    events.open(std::filesystem::path(module).parent_path()/L"fixture-events.jsonl",std::ios::app);
    WNDCLASSW cls{};cls.hInstance=instance;cls.lpfnWndProc=WindowProc;
    cls.lpszClassName=L"GlassDesk.QA.WindowFixture";cls.hCursor=LoadCursor(nullptr,IDC_ARROW);
    if(!RegisterClassW(&cls))return 1;
    bool topmost=std::wstring_view(arguments?arguments:L"").find(L"--topmost")!=std::wstring_view::npos;
    backdropOnly=std::wstring_view(arguments?arguments:L"").find(L"--backdrop")!=std::wstring_view::npos;
    HWND window=CreateWindowExW((topmost?WS_EX_TOPMOST:0)|(backdropOnly?WS_EX_NOACTIVATE:0),cls.lpszClassName,L"GlassDesk QA Window Fixture",WS_OVERLAPPEDWINDOW,
        topmost?800:220,topmost?36:300,topmost?960:900,topmost?980:550,nullptr,nullptr,instance,nullptr);
    if(!window)return 2;
    ShowWindow(window,SW_SHOWNOACTIVATE);Record("start",window);
    MSG message{};
    while(GetMessageW(&message,nullptr,0,0)>0) {TranslateMessage(&message);DispatchMessageW(&message);}
    events << "{\"event\":\"clean-shutdown\"}" << std::endl;
    return 0;
}
