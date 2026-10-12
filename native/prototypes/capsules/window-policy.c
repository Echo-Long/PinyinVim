// The hook installs a subclass INSIDE Neovide's window thread. No global
// hook, cross-process WndProc pointer or additional visible window is used.
#define UNICODE
#define _UNICODE
#define _WIN32_WINNT 0x0601
#include <windows.h>
#include <windowsx.h>
#include <math.h>
#include <string.h>
#include "desktop-effects.h"

static HMODULE module;
static const wchar_t *box_keys[] = { L"PinyinCapsules.EditorBounds", L"PinyinCapsules.MessageBounds", L"PinyinCapsules.ModeBounds", L"PinyinCapsules.PendingBounds", L"PinyinCapsules.SettingsBounds", L"PinyinCapsules.SubmitBounds" };
#define CAPTION_STYLES (WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX)
typedef struct { RECT bounds; POINT origin; int hit; } PointerGesture;
static int value(HWND window, const wchar_t *key) {
    return (int)(INT_PTR)GetPropW(window, key);
}
static int hidden_test(HWND window) {
    // Only an explicitly authorized desktop-pixel fixture may show a test
    // surface. Ordinary contracts remain hidden throughout their lifetime.
    return value(window, L"PinyinCapsules.TestInstance") &&
        !value(window, L"PinyinCapsules.PixelTestVisible");
}
static UINT setup_message(void) {
    static UINT message;
    if (!message) message = RegisterWindowMessageW(L"PinyinVim.Capsules.SetupWindowPolicy");
    return message;
}
static UINT backdrop_message, foreground_message;
#define IMAGE_COPYDATA ((ULONG_PTR)0x50564D49)
static double zoom(HWND window, int width) {
    (void)window; (void)width;
    return 1; // Resizing adds rows/columns; it never changes the font.
}
static int row_height(HWND window, int width) {
    int height = value(window, L"PinyinCapsules.BaseRowHeight");
    int linespace = value(window, L"PinyinCapsules.LineSpace");
    if (height <= linespace) return 0;
    return (int)ceil((height - linespace) * zoom(window, width) + linespace);
}
static double spacing(HWND window, int width) {
    double column = value(window, L"PinyinCapsules.BaseCellWidth1000") / 1000.0;
    int client_width = width - value(window, L"PinyinCapsules.FrameX");
    if (column <= 0) return 0;
    int columns = (int)floor((client_width - 1) / column + .01);
    return column + (client_width - columns * column) / 6;
}
static int minimum_height(HWND window, int width) {
    double gap = spacing(window, width);
    return (int)ceil(4 * row_height(window, width) + 3 * gap) + value(window, L"PinyinCapsules.FrameY");
}
static int inside_box(HWND window, int index, POINT point) {
    RECT *box = (RECT *)GetPropW(window, box_keys[index]);
    if (!box || !PtInRect(box, point)) return 0;
    double radius = value(window, L"PinyinCapsules.BaseRowHeight");
    radius = fmin(radius, (box->bottom - box->top) / 2.0);
    radius = fmin(radius, (box->right - box->left) / 2.0);
    double cx = fmax(box->left + radius, fmin(point.x, box->right - radius));
    double cy = fmax(box->top + radius, fmin(point.y, box->bottom - radius));
    double dx = point.x - cx, dy = point.y - cy;
    return dx * dx + dy * dy <= radius * radius;
}
static void update_hover(HWND window, int panel) {
    if (panel == value(window, L"PinyinCapsules.HoverPanel")) return;
    SetPropW(window, L"PinyinCapsules.HoverPanel", (HANDLE)(INT_PTR)panel);
    HWND host = (HWND)GetPropW(window, L"PinyinCapsules.Host");
    if (host && IsWindow(host)) PostMessageW(host,
        RegisterWindowMessageW(L"PinyinVim.Capsules.HoverChanged"), panel, (LPARAM)window);
}
static void pointer_hover(HWND window, LPARAM position) {
    POINT point = { GET_X_LPARAM(position), GET_Y_LPARAM(position) };
    int panel = 0;
    for (int i = 2; i < 6; ++i) if (inside_box(window, i, point)) { panel = i - 1; break; }
    update_hover(window, panel);
    if (IsWindowVisible(window)) {
        TRACKMOUSEEVENT tracking = { sizeof(TRACKMOUSEEVENT), TME_LEAVE, window, HOVER_DEFAULT };
        TrackMouseEvent(&tracking);
    }
}
static LRESULT hit_test(HWND window, LPARAM position) {
    RECT bounds; GetWindowRect(window, &bounds);
    int x = GET_X_LPARAM(position) - bounds.left, y = GET_Y_LPARAM(position) - bounds.top;
    int width = bounds.right - bounds.left, height = bounds.bottom - bounds.top;
    int edge = value(window, L"PinyinCapsules.Edge");
    int corner = 2 * edge;
    // Corner grips are small. The whole rounded-corner square must never
    // consume the shell's drag surface or overlap the editor's mouse area.
    if (x < corner && y < corner) return HTTOPLEFT;
    if (x >= width - corner && y < corner) return HTTOPRIGHT;
    if (x < corner && y >= height - corner) return HTBOTTOMLEFT;
    if (x >= width - corner && y >= height - corner) return HTBOTTOMRIGHT;
    int left = x < edge, right = x >= width - edge;
    int top = y < edge, bottom = y >= height - edge;
    if (top && left) return HTTOPLEFT;
    if (top && right) return HTTOPRIGHT;
    if (bottom && left) return HTBOTTOMLEFT;
    if (bottom && right) return HTBOTTOMRIGHT;
    if (left) return HTLEFT;
    if (right) return HTRIGHT;
    if (top) return HTTOP;
    if (bottom) return HTBOTTOM;
    POINT point = { GET_X_LPARAM(position), GET_Y_LPARAM(position) };
    ScreenToClient(window, &point);
    for (int i = 0; i < 6; ++i) {
        if (inside_box(window, i, point)) return HTCLIENT;
    }
    return HTCAPTION;
}
static void clamp_size(HWND window, RECT *bounds, WPARAM edge) {
    int minimum = value(window, L"PinyinCapsules.MinWidth");
    int width = bounds->right - bounds->left;
    if (width < minimum) {
        if (edge == WMSZ_LEFT || edge == WMSZ_TOPLEFT || edge == WMSZ_BOTTOMLEFT)
            bounds->left = bounds->right - minimum;
        else bounds->right = bounds->left + minimum;
        width = minimum;
    }
    int height = minimum_height(window, width);
    if (bounds->bottom - bounds->top < height) {
        if (edge == WMSZ_TOP || edge == WMSZ_TOPLEFT || edge == WMSZ_TOPRIGHT)
            bounds->top = bounds->bottom - height;
        else bounds->bottom = bounds->top + height;
    }
}
static void clear_boxes(HWND window) {
    for (int i = 0; i < 6; ++i) {
        void *box = RemovePropW(window, box_keys[i]);
        if (box) HeapFree(GetProcessHeap(), 0, box);
    }
}
static void replace_image_path(HWND window, const wchar_t *path, size_t bytes) {
    wchar_t *copy = NULL;
    if (path && bytes >= sizeof(wchar_t)) {
        size_t chars = bytes / sizeof(wchar_t);
        if (chars > 4095) chars = 4095;
        copy = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, (chars + 1) * sizeof(wchar_t));
        if (copy) {
            memcpy(copy, path, chars * sizeof(wchar_t)); copy[chars] = L'\0';
        }
    }
    wchar_t *old = (wchar_t *)RemovePropW(window, L"PinyinCapsules.BackgroundImagePath");
    if (old) HeapFree(GetProcessHeap(), 0, old);
    if (copy) SetPropW(window, L"PinyinCapsules.BackgroundImagePath", (HANDLE)copy);
}
static void end_pointer_gesture(HWND window, int restore) {
    PointerGesture *gesture = RemovePropW(window, L"PinyinCapsules.PointerGesture");
    if (!gesture) return;
    RemovePropW(window, L"PinyinCapsules.PointerEdge");
    if (restore) SetWindowPos(window, NULL, gesture->bounds.left, gesture->bounds.top,
        gesture->bounds.right - gesture->bounds.left, gesture->bounds.bottom - gesture->bounds.top,
        SWP_NOACTIVATE | SWP_NOZORDER);
    HeapFree(GetProcessHeap(), 0, gesture);
    if (GetCapture() == window) ReleaseCapture();
}
static void begin_pointer_gesture(HWND window, int hit, LPARAM position) {
    end_pointer_gesture(window, 0);
    PointerGesture *gesture = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(PointerGesture));
    if (!gesture) return;
    GetWindowRect(window, &gesture->bounds);
    gesture->origin.x = GET_X_LPARAM(position); gesture->origin.y = GET_Y_LPARAM(position);
    gesture->hit = hit;
    SetPropW(window, L"PinyinCapsules.PointerGesture", gesture);
    SetPropW(window, L"PinyinCapsules.PointerEdge", (HANDLE)(INT_PTR)hit);
    // Hidden protocol fixtures exercise this same event path without taking
    // system mouse capture or activating a window on the user's desktop.
    if (!value(window, L"PinyinCapsules.TestInstance")) SetCapture(window);
}
static void update_pointer_gesture(HWND window, PointerGesture *gesture, LPARAM position) {
    POINT pointer = { GET_X_LPARAM(position), GET_Y_LPARAM(position) };
    ClientToScreen(window, &pointer);
    int dx = pointer.x - gesture->origin.x, dy = pointer.y - gesture->origin.y;
    RECT next = gesture->bounds;
    int hit = gesture->hit;
    if (hit == HTCAPTION) OffsetRect(&next, dx, dy);
    else {
        if (hit == HTLEFT || hit == HTTOPLEFT || hit == HTBOTTOMLEFT) next.left += dx;
        if (hit == HTRIGHT || hit == HTTOPRIGHT || hit == HTBOTTOMRIGHT) next.right += dx;
        if (hit == HTTOP || hit == HTTOPLEFT || hit == HTTOPRIGHT) next.top += dy;
        if (hit == HTBOTTOM || hit == HTBOTTOMLEFT || hit == HTBOTTOMRIGHT) next.bottom += dy;
        static const WPARAM sizing_edges[] = { WMSZ_LEFT, WMSZ_RIGHT, WMSZ_TOP, WMSZ_TOPLEFT,
            WMSZ_TOPRIGHT, WMSZ_BOTTOM, WMSZ_BOTTOMLEFT, WMSZ_BOTTOMRIGHT };
        clamp_size(window, &next, sizing_edges[hit - HTLEFT]);
    }
    // Return after each position update. DefWindowProc's SC_SIZE/SC_MOVE loop
    // would block Neovide 0.16.2's AboutToWait -> prepare_frame -> Present path.
    SetWindowPos(window, NULL, next.left, next.top, next.right - next.left, next.bottom - next.top,
        SWP_NOACTIVATE | SWP_NOZORDER);
    SetPropW(window, L"PinyinCapsules.PointerUpdates",
        (HANDLE)(INT_PTR)(value(window, L"PinyinCapsules.PointerUpdates") + 1));
}
static LRESULT CALLBACK capsule_proc(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
    WNDPROC original = (WNDPROC)GetPropW(window, L"PinyinCapsules.OriginalProc");
    if (message == WM_COPYDATA) {
        COPYDATASTRUCT *packet = (COPYDATASTRUCT *)lparam;
        if (packet && (ULONG_PTR)packet->dwData == IMAGE_COPYDATA) {
            replace_image_path(window, (const wchar_t *)packet->lpData, (size_t)packet->cbData);
            DesktopBackdrop *backdrop = GetPropW(window, L"PinyinCapsules.BackdropObjects");
            if (backdrop) backdrop_replace_image(backdrop, backdrop_image_path(window));
            backdrop_apply(window, value(window, L"PinyinCapsules.BlurRequested"));
            return 1;
        }
    }
    if (message == WM_SIZE) backdrop_resize(window);
    if (!backdrop_message) backdrop_message = RegisterWindowMessageW(L"PinyinVim.Capsules.ApplyHostBackdrop");
    if (!foreground_message) foreground_message = RegisterWindowMessageW(L"PinyinVim.Capsules.GrantSettingsForeground");
    if (message == backdrop_message) {
        backdrop_apply(window, wparam != 0);
        return value(window, L"PinyinCapsules.DesktopBlur");
    }
    if (message == foreground_message) {
        HWND host = GetPropW(window, L"PinyinCapsules.Host");
        DWORD host_pid = 0, foreground_pid = 0;
        if (host && IsWindow(host)) GetWindowThreadProcessId(host, &host_pid);
        GetWindowThreadProcessId(GetForegroundWindow(), &foreground_pid);
        // Only the process receiving the user's click can transfer its
        // foreground permission. Never steal focus after the user switches apps.
        int granted = !value(window, L"PinyinCapsules.TestInstance") &&
            host_pid && foreground_pid == GetCurrentProcessId() && AllowSetForegroundWindow(host_pid);
        SetPropW(window, L"PinyinCapsules.ForegroundRequests",
            (HANDLE)(INT_PTR)(value(window, L"PinyinCapsules.ForegroundRequests") + 1));
        SetPropW(window, L"PinyinCapsules.ForegroundGranted", (HANDLE)(INT_PTR)granted);
        return granted;
    }
    if (message == setup_message()) {
        if (wparam == 1) {
            // Both installation and restoration run on the owner thread.
            if (original) SetWindowLongPtrW(window, GWLP_WNDPROC, (LONG_PTR)original);
            RemovePropW(window, L"PinyinCapsules.OriginalProc");
            end_pointer_gesture(window, 0);
            clear_boxes(window);
            backdrop_destroy(window);
        } else {
            LONG_PTR style = GetWindowLongPtrW(window, GWL_STYLE);
            // The capsule policy owns all edge/corner hit testing and the
            // nonmodal pointer gesture. Keep the Winit surface free of a
            // system thick frame so DWM cannot paint a non-client strip.
            LONG_PTR desired = style & ~(CAPTION_STYLES | WS_THICKFRAME);
            if (hidden_test(window)) desired &= ~WS_VISIBLE;
            if (style != desired) {
                SetWindowLongPtrW(window, GWL_STYLE, desired);
                SetWindowPos(window, NULL, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            // The host sends numeric coordinates, never cross-process pointers.
            static const wchar_t *names[] = { L"Editor", L"Message", L"Mode", L"Pending", L"Settings", L"Submit" };
            for (int i = 0; i < 6; ++i) {
                RECT *rect = (RECT *)GetPropW(window, box_keys[i]);
                if (!rect) {
                    rect = (RECT *)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(RECT));
                    SetPropW(window, box_keys[i], (HANDLE)rect);
                }
                if (rect) {
                    wchar_t key[80];
                    wsprintfW(key, L"PinyinCapsules.%sLeft", names[i]); rect->left = value(window, key);
                    wsprintfW(key, L"PinyinCapsules.%sTop", names[i]); rect->top = value(window, key);
                    wsprintfW(key, L"PinyinCapsules.%sRight", names[i]); rect->right = value(window, key);
                    wsprintfW(key, L"PinyinCapsules.%sBottom", names[i]); rect->bottom = value(window, key);
                }
            }
            /* Host geometry updates the footer boxes after every client
               resize. Reposition the exact remainder overlay after those
               coordinates are current, without rebuilding its effect. */
            backdrop_resize(window);
        }
        return 0;
    }
    if (message == WM_NCHITTEST) return hit_test(window, lparam);
    if (message == WM_MOUSEACTIVATE && value(window, L"PinyinCapsules.TestInstance")) return MA_NOACTIVATE;
    if (message == WM_ENTERSIZEMOVE) SetPropW(window, L"PinyinCapsules.ModalEntries",
        (HANDLE)(INT_PTR)(value(window, L"PinyinCapsules.ModalEntries") + 1));
    if (message == WM_NCLBUTTONDOWN && (wparam == HTCAPTION || (wparam >= HTLEFT && wparam <= HTBOTTOMRIGHT))) {
        begin_pointer_gesture(window, (int)wparam, lparam);
        return 0;
    }
    PointerGesture *gesture = GetPropW(window, L"PinyinCapsules.PointerGesture");
    if (gesture) {
        if (message == WM_MOUSEMOVE) {
            if (wparam & MK_LBUTTON) update_pointer_gesture(window, gesture, lparam);
            else end_pointer_gesture(window, 0);
            return 0;
        }
        if (message == WM_LBUTTONUP) { end_pointer_gesture(window, 0); return 0; }
        if (message == WM_KEYDOWN && wparam == VK_ESCAPE) { end_pointer_gesture(window, 1); return 0; }
        if (message == WM_CAPTURECHANGED || message == WM_CANCELMODE ||
            (message == WM_ACTIVATEAPP && !wparam) || (message == WM_SHOWWINDOW && !wparam))
            end_pointer_gesture(window, 0);
    }
    // Neovide 0.16.2 deduplicates motion by local row/column, ignoring a
    // change of grid. Adjacent button centres can therefore miss MouseMove.
    // Report only panel transitions using these same owner-thread rectangles.
    if (message == WM_MOUSEMOVE || message == WM_LBUTTONDOWN) pointer_hover(window, lparam);
    // Winit also requests leave tracking. A hidden HWND is always physically
    // outside the pointer, so its automatic leave is unrelated to injected
    // client protocol used by hidden contract fixtures.
    if ((message == WM_MOUSELEAVE && IsWindowVisible(window)) || message == WM_NCMOUSEMOVE ||
        (message == WM_SHOWWINDOW && !wparam) || (message == WM_ACTIVATEAPP && !wparam)) update_hover(window, 0);
    if (message == WM_CLOSE || (message == WM_SYSCOMMAND && (wparam & 0xfff0) == SC_MINIMIZE)) {
        HWND host = (HWND)GetPropW(window, L"PinyinCapsules.Host");
        if (host && IsWindow(host)) {
            PostMessageW(host, RegisterWindowMessageW(L"PinyinVim.Capsules.HideRequested"), 0, (LPARAM)window);
            return 0;
        }
    }
    if (message == WM_NCLBUTTONDBLCLK && wparam == HTCAPTION) return 0;
    if (message == WM_NCCALCSIZE && wparam) return 0;
    // Winit still receives WINDOWPOS/SIZE/DPI events and remains the renderer.
    if (message == WM_WINDOWPOSCHANGING) {
        WINDOWPOS *position = (WINDOWPOS *)lparam;
        if (position) {
            SetPropW(window, L"PinyinCapsules.WindowPosCalls",
                (HANDLE)(INT_PTR)(value(window, L"PinyinCapsules.WindowPosCalls") + 1));
            SetPropW(window, L"PinyinCapsules.WindowPosRequestedWidth", (HANDLE)(INT_PTR)position->cx);
            SetPropW(window, L"PinyinCapsules.WindowPosRequestedHeight", (HANDLE)(INT_PTR)position->cy);
        }
        // Winit can call SW_SHOW again while updating cached window flags.
        // Disposable fixtures must remain nonactivating even when pixels show.
        if (value(window, L"PinyinCapsules.TestInstance")) position->flags |= SWP_NOACTIVATE;
        if (hidden_test(window)) position->flags &= ~SWP_SHOWWINDOW;
        if (!(position->flags & SWP_NOSIZE)) {
            int minimum = value(window, L"PinyinCapsules.MinWidth");
            if (position->cx < minimum) position->cx = minimum;
            double column = value(window, L"PinyinCapsules.BaseCellWidth1000") / 1000.0;
            int frame_x = value(window, L"PinyinCapsules.FrameX");
            if (column > 0) {
                int columns = (int)floor((position->cx - frame_x - 1) / column + .5);
                if (columns < 45) columns = 45;
                // Keep a device-pixel remainder so the right float does not
                // enter Neovide's rectangular trailing-fill path.
                position->cx = (int)ceil(columns * column) + 1 + frame_x;
                if (position->cx < minimum) position->cx = minimum;
            }
            int cell = row_height(window, position->cx);
            if (cell > 0) {
                int frame = value(window, L"PinyinCapsules.FrameY");
                double gap = spacing(window, position->cx);
                double rows = floor((position->cy - frame - 3 * gap) / cell - 3 + .5);
                if (rows < 1) rows = 1;
                position->cy = (int)ceil((rows + 3) * cell + 3 * gap) + frame;
            }
            if (position) {
                SetPropW(window, L"PinyinCapsules.WindowPosFinalWidth", (HANDLE)(INT_PTR)position->cx);
                SetPropW(window, L"PinyinCapsules.WindowPosFinalHeight", (HANDLE)(INT_PTR)position->cy);
            }
            if (gesture) {
                int hit = gesture->hit;
                if (hit == HTLEFT || hit == HTTOPLEFT || hit == HTBOTTOMLEFT)
                    position->x = gesture->bounds.right - position->cx;
                if (hit == HTTOP || hit == HTTOPLEFT || hit == HTTOPRIGHT)
                    position->y = gesture->bounds.bottom - position->cy;
            }
        }
    }
    LRESULT result = original ? CallWindowProcW(original, window, message, wparam, lparam)
        : DefWindowProcW(window, message, wparam, lparam);
    if (message == WM_STYLECHANGING && (int)wparam == GWL_STYLE) {
        // Winit's setters can restore its cached caption flags. Enforce the
        // invariant on the owner thread before the style is committed.
        STYLESTRUCT *styles = (STYLESTRUCT *)lparam;
        styles->styleNew &= ~(CAPTION_STYLES | WS_THICKFRAME);
        if (hidden_test(window)) styles->styleNew &= ~WS_VISIBLE;
    } else if (message == WM_STYLECHANGING && (int)wparam == GWL_EXSTYLE && value(window, L"PinyinCapsules.TestInstance")) {
        ((STYLESTRUCT *)lparam)->styleNew |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
    } else if (message == WM_GETMINMAXINFO && value(window, L"PinyinCapsules.MinWidth") > 0) {
        RECT bounds; GetWindowRect(window, &bounds);
        MINMAXINFO *info = (MINMAXINFO *)lparam;
        info->ptMinTrackSize.x = value(window, L"PinyinCapsules.MinWidth");
        info->ptMinTrackSize.y = minimum_height(window, bounds.right - bounds.left);
    } else if (message == WM_SIZING) {
        clamp_size(window, (RECT *)lparam, wparam);
    } else if (message == WM_NCDESTROY) {
        end_pointer_gesture(window, 0);
        clear_boxes(window);
        backdrop_destroy(window);
        wchar_t *image = (wchar_t *)RemovePropW(window, L"PinyinCapsules.BackgroundImagePath");
        if (image) HeapFree(GetProcessHeap(), 0, image);
        RemovePropW(window, L"PinyinCapsules.OriginalProc");
    }
    return result;
}
static LRESULT CALLBACK bootstrap(int code, WPARAM wparam, LPARAM lparam) {
    if (code >= 0) {
        const CWPRETSTRUCT *message = (const CWPRETSTRUCT *)lparam;
        if (message->message == setup_message() && !message->wParam &&
            value(message->hwnd, L"PinyinCapsules.Edge") > 0) {
            if (!GetPropW(message->hwnd, L"PinyinCapsules.OriginalProc")) {
                WNDPROC original = (WNDPROC)GetWindowLongPtrW(message->hwnd, GWLP_WNDPROC);
                SetPropW(message->hwnd, L"PinyinCapsules.OriginalProc", (HANDLE)original);
                SetWindowLongPtrW(message->hwnd, GWLP_WNDPROC, (LONG_PTR)capsule_proc);
                SendMessageW(message->hwnd, setup_message(), 0, 0);
            }
        }
    }
    return CallNextHookEx(NULL, code, wparam, lparam);
}
__declspec(dllexport) HHOOK InstallCapsulePolicy(HWND window) {
    return SetWindowsHookExW(WH_CALLWNDPROCRET, bootstrap, module, GetWindowThreadProcessId(window, NULL));
}
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved) {
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) { module = instance; DisableThreadLibraryCalls(instance); }
    return TRUE;
}
