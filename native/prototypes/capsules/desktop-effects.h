// Windows.UI.Composition ABI, isolated from the editor/input policy.
// Interface IDs and method order: Microsoft windows-rs 0.58.0,
// Windows/UI/Composition and Windows/System; Desktop interop: windows.ui.composition.interop.h.
// This code MUST run on the HWND's own thread, inside its process. A target
// created by the host process fails with E_ACCESSDENIED, even for an owned HWND.
#include <dwmapi.h>
#define COBJMACROS
#define INITGUID
#define WIDL_using_Windows_UI_Composition
#include <windows.ui.composition.h>
#include <windows.ui.composition.interop.h>
#include <d3d11.h>
#include <dxgi.h>
#include <dxgi1_2.h>
#include <wincodec.h>

typedef struct PvCom { void **methods; } PvCom;
typedef HRESULT (WINAPI *PvQuery)(PvCom *, const GUID *, PvCom **);
typedef ULONG (WINAPI *PvRelease)(PvCom *);
typedef HRESULT (WINAPI *PvCreate)(PvCom *, PvCom **);
typedef HRESULT (WINAPI *PvSetObject)(PvCom *, PvCom *);
typedef struct { float x, y; } PvVector2;
typedef HRESULT (WINAPI *PvSetSize)(PvCom *, PvVector2);
typedef HRESULT (WINAPI *PvInsertVisual)(PvCom *, PvCom *);
typedef HRESULT (WINAPI *PvCreateTarget)(PvCom *, HWND, BOOL, PvCom **);
typedef HRESULT (WINAPI *PvClose)(PvCom *);
typedef struct { DWORD size, thread, apartment; } PvQueueOptions;
typedef HRESULT (WINAPI *PvCreateQueue)(PvQueueOptions, PvCom **);
typedef HRESULT (WINAPI *PvRoInitialize)(UINT);
typedef void (WINAPI *PvRoUninitialize)(void);
typedef HRESULT (WINAPI *PvString)(PCWSTR, UINT, void **);
typedef HRESULT (WINAPI *PvDeleteString)(void *);
typedef HRESULT (WINAPI *PvActivate)(void *, PvCom **);
/* Do not use MinGW's ICompositorInterop macro here. Its shipped IDL
   incorrectly derives this interface from IInspectable. Windows implements
   the documented IUnknown ABI, so the composition methods start at slot 3. */
typedef HRESULT (WINAPI *PvCreateCompositionSurfaceForSwapChain)(PvCom *, IUnknown *, PvCom **);

static const GUID pv_compositor = {0xb403ca50,0x7f8c,0x4e83,{0x98,0x5f,0xcc,0x45,0x06,0x00,0x36,0xd8}};
static const GUID pv_compositor2 = {0x735081dc,0x5e24,0x45da,{0xa3,0x8f,0xe3,0x2c,0xc3,0x49,0xa9,0xa0}};
static const GUID pv_desktop = {0x29e691fa,0x4567,0x4dca,{0xb3,0x19,0xd0,0xf2,0x07,0xeb,0x68,0x07}};
static const GUID pv_visual = {0x117e202d,0xa859,0x4c89,{0x87,0x3b,0xc2,0xaa,0x56,0x67,0x88,0xe3}};
static const GUID pv_container_visual = {0x02f6bc74,0xed20,0x4773,{0xaf,0xe6,0xd4,0x9b,0x4a,0x93,0xdb,0x32}};
static const GUID pv_brush = {0xab0d7608,0x30c0,0x40e9,{0xb5,0x68,0xb6,0x0a,0x6b,0xd1,0xfb,0x46}};
static const GUID pv_target = {0xa1bea8ba,0xd726,0x4663,{0x81,0x29,0x6b,0x5e,0x79,0x27,0xff,0xa6}};
static const GUID pv_closable = {0x30d5a829,0x7fa4,0x4026,{0x83,0xbb,0xd7,0x5b,0xae,0x4e,0xa9,0x9e}};
static const GUID pv_compositor_interop = {0x25297d5c,0x3ad4,0x4c9c,{0xb5,0xcf,0xe3,0x6a,0x38,0x51,0x23,0x30}};
/* Keep these GUIDs local. MinGW's import libraries do not export all of the
   DXGI/D3D interface IDs even though the headers declare them. */
static const GUID pv_iid_d3d11_texture2d = {0x6f15aaf2,0xd208,0x4e89,{0x9a,0xb4,0x48,0x95,0x35,0xd3,0x4f,0x9c}};
static const GUID pv_iid_dxgi_device = {0x54ec77fa,0x1377,0x44e6,{0x8c,0x32,0x88,0xfd,0x5f,0x44,0xc8,0x4c}};
static const GUID pv_iid_dxgi_factory2 = {0x50c83a1c,0xe072,0x4c48,{0x87,0xb0,0x36,0x30,0xfa,0x36,0xa6,0xd0}};

typedef struct {
    HMODULE runtime, messaging;
    int initialized, enabled;
    PvRoUninitialize uninitialize;
    PvCom *queue, *compositor, *base, *sprite, *visual, *input, *factory, *brush, *desktop_target, *target;
    PvCom *container, *children, *desktop_visual;
    PvCom *image_factory, *image_effect, *image_sprite, *image_visual;
    ID3D11Device *image_device;
    ID3D11DeviceContext *image_context;
    IDXGISwapChain1 *image_swapchain;
    PvCom *image_surface, *image_brush;
    /* The Neovide swapchain has a fractional client remainder after the
       last complete grid row. A small click-through composition child covers
       only that remainder with the same desktop/image effect. It never
       touches editor, message, or control pixels. */
    HWND overlay_window;
    PvCom *overlay_target, *overlay_target_root, *overlay_container,
        *overlay_children, *overlay_sprite, *overlay_desktop_visual,
        *overlay_visual, *overlay_image_sprite, *overlay_image_visual;
    int image_active;
    float sigma;
} DesktopBackdrop;

static HRESULT pv_query(PvCom *object, const GUID *id, PvCom **result) {
    return ((PvQuery)object->methods[0])(object, id, result);
}
static void pv_release(PvCom *object) {
    if (object) ((PvRelease)object->methods[2])(object);
}
#include "gaussian-effect.h"

static void pv_close(PvCom *object);
static void backdrop_release_image_effect(DesktopBackdrop *state) {
    if (!state) return;
    pv_close(state->image_effect); pv_release(state->image_effect);
    pv_close(state->image_factory); pv_release(state->image_factory);
    pv_close(state->image_sprite); pv_release(state->image_visual); pv_release(state->image_sprite);
    state->image_effect = NULL; state->image_factory = NULL;
    state->image_visual = NULL; state->image_sprite = NULL;
}
static void backdrop_release_image_parts(DesktopBackdrop *state) {
    if (!state) return;
    pv_close(state->image_brush); pv_release(state->image_brush);
    pv_release(state->image_surface);
    if (state->image_swapchain) IDXGISwapChain1_Release(state->image_swapchain);
    if (state->image_context) ID3D11DeviceContext_Release(state->image_context);
    if (state->image_device) ID3D11Device_Release(state->image_device);
    state->image_brush = NULL; state->image_surface = NULL; state->image_swapchain = NULL;
    state->image_context = NULL; state->image_device = NULL; state->image_active = 0;
}

static void pv_close(PvCom *object) {
    PvCom *close = NULL;
    if (object && SUCCEEDED(pv_query(object, &pv_closable, &close))) {
        ((PvClose)close->methods[6])(close);
        pv_release(close);
    }
}
static LRESULT CALLBACK backdrop_overlay_proc(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
    (void)window; (void)wparam; (void)lparam;
    if (message == WM_NCHITTEST) return HTTRANSPARENT;
    if (message == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
    return DefWindowProcW(window, message, wparam, lparam);
}
static HWND backdrop_overlay_window(HWND parent) {
    static ATOM atom;
    if (!atom) {
        WNDCLASSW klass;
        ZeroMemory(&klass, sizeof(klass));
        klass.lpfnWndProc = backdrop_overlay_proc;
        klass.hInstance = GetModuleHandleW(NULL);
        klass.lpszClassName = L"PinyinVim.CapsuleBackdropRemainder";
        atom = RegisterClassW(&klass);
        if (!atom && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return NULL;
    }
    return CreateWindowExW(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT,
        L"PinyinVim.CapsuleBackdropRemainder", L"", WS_CHILD | WS_VISIBLE,
        0, 0, 1, 1, parent, NULL, GetModuleHandleW(NULL), NULL);
}
static void backdrop_overlay_region(HWND window, int width, int height) {
    if (!window || width < 1 || height < 1) return;
    int radius = (int)(INT_PTR)GetPropW(window, L"PinyinCapsules.Edge");
    if (radius < 2) radius = 2;
    if (radius * 2 > height) radius = height / 2;
    if (radius < 1) radius = 1;
    /* Keep the top edge rectangular and round only the two bottom corners.
       The child begins at the footer's bottom, so this is the exact lower
       part of the owner's DWM silhouette. */
    HRGN body = CreateRectRgn(0, 0, width, height - radius);
    HRGN lower = CreateRoundRectRgn(0, height - radius * 2, width + 1, height + 1,
        radius * 2, radius * 2);
    if (body && lower) {
        CombineRgn(body, body, lower, RGN_OR);
        SetWindowRgn(window, body, TRUE);
    } else if (body) DeleteObject(body);
    if (lower) DeleteObject(lower);
}
static void backdrop_overlay_destroy(DesktopBackdrop *state) {
    if (!state) return;
    if (state->overlay_target)
        ((PvSetObject)state->overlay_target->methods[7])(state->overlay_target, NULL);
    pv_close(state->overlay_target); pv_release(state->overlay_target);
    pv_close(state->overlay_children); pv_release(state->overlay_children);
    pv_close(state->overlay_container); pv_release(state->overlay_visual); pv_release(state->overlay_container);
    pv_close(state->overlay_sprite); pv_release(state->overlay_desktop_visual); pv_release(state->overlay_sprite);
    pv_close(state->overlay_image_sprite); pv_release(state->overlay_image_visual); pv_release(state->overlay_image_sprite);
    if (state->overlay_window) DestroyWindow(state->overlay_window);
    state->overlay_window = NULL;
    state->overlay_target = state->overlay_target_root = state->overlay_container = NULL;
    state->overlay_children = state->overlay_sprite = state->overlay_desktop_visual = state->overlay_visual = NULL;
    state->overlay_image_sprite = state->overlay_image_visual = NULL;
}
static HRESULT backdrop_overlay_ensure_image_visual(DesktopBackdrop *state) {
    if (!state || !state->overlay_children || !state->image_effect) return E_INVALIDARG;
    if (state->overlay_image_sprite) {
        return ((PvSetObject)state->overlay_image_sprite->methods[7])(
            state->overlay_image_sprite, state->image_effect);
    }
    HRESULT hr = ((PvCreate)state->base->methods[22])(state->base, &state->overlay_image_sprite);
    if (FAILED(hr)) return hr;
    hr = pv_query(state->overlay_image_sprite, &pv_visual, &state->overlay_image_visual);
    if (SUCCEEDED(hr)) hr = ((PvSetObject)state->overlay_image_sprite->methods[7])(
        state->overlay_image_sprite, state->image_effect);
    if (SUCCEEDED(hr)) hr = ((PvInsertVisual)state->overlay_children->methods[9])(
        state->overlay_children, state->overlay_image_visual);
    if (FAILED(hr)) {
        pv_close(state->overlay_image_sprite); pv_release(state->overlay_image_visual); pv_release(state->overlay_image_sprite);
        state->overlay_image_visual = NULL; state->overlay_image_sprite = NULL;
    }
    return hr;
}
static void backdrop_dispose(DesktopBackdrop *state) {
    if (!state) return;
    backdrop_overlay_destroy(state);
    if (state->target) ((PvSetObject)state->target->methods[7])(state->target, NULL);
    pv_close(state->desktop_target); pv_release(state->target); pv_release(state->desktop_target);
    pv_close(state->children); pv_release(state->children);
    pv_close(state->container); pv_release(state->visual); pv_release(state->container);
    pv_close(state->sprite); pv_release(state->desktop_visual); pv_release(state->sprite);
    pv_close(state->brush); pv_release(state->brush);
    pv_close(state->factory); pv_release(state->factory);
    pv_close(state->input); pv_release(state->input);
    backdrop_release_image_effect(state);
    backdrop_release_image_parts(state);
    pv_release(state->base);
    pv_close(state->compositor); pv_release(state->compositor);
    if (state->queue) {
        PvCom *shutdown = NULL;
        ((PvCreate)state->queue->methods[7])(state->queue, &shutdown); // ShutdownQueueAsync
        pv_release(shutdown); pv_release(state->queue);
    }
    if (state->initialized && state->uninitialize) state->uninitialize();
    if (state->messaging) FreeLibrary(state->messaging);
    if (state->runtime) FreeLibrary(state->runtime);
    HeapFree(GetProcessHeap(), 0, state);
}
static int backdrop_value(HWND window, const wchar_t *key) {
    return (int)(INT_PTR)GetPropW(window, key);
}
static const wchar_t *backdrop_image_path(HWND window) {
    return (const wchar_t *)GetPropW(window, L"PinyinCapsules.BackgroundImagePath");
}
static HRESULT backdrop_image_brush(DesktopBackdrop *state, const wchar_t *path, PvCom **result) {
    if (!path || !path[0] || !result) return E_INVALIDARG;
    *result = NULL;
    HRESULT hr;
    IWICImagingFactory *wic = NULL;
    IWICBitmapDecoder *decoder = NULL;
    IWICBitmapFrameDecode *frame = NULL;
    IWICFormatConverter *converter = NULL;
    IDXGIDevice *dxgi_device = NULL;
    IDXGIAdapter *adapter = NULL;
    IDXGIFactory2 *dxgi_factory = NULL;
    ID3D11Texture2D *backbuffer = NULL, *backbuffer1 = NULL;
    PvCom *interopObject = NULL;
    UINT width = 0, height = 0;
    BYTE *pixels = NULL;
    hr = CoCreateInstance(&CLSID_WICImagingFactory, NULL, CLSCTX_INPROC_SERVER,
        &IID_IWICImagingFactory, (void **)&wic);
    if (FAILED(hr)) goto done;
    hr = IWICImagingFactory_CreateDecoderFromFilename(wic, path, NULL, GENERIC_READ,
        WICDecodeMetadataCacheOnLoad, &decoder);
    if (FAILED(hr)) goto done;
    hr = IWICBitmapDecoder_GetFrame(decoder, 0, &frame); if (FAILED(hr)) goto done;
    hr = IWICImagingFactory_CreateFormatConverter(wic, &converter); if (FAILED(hr)) goto done;
    hr = IWICFormatConverter_Initialize(converter, (IWICBitmapSource *)frame,
        &GUID_WICPixelFormat32bppPBGRA, WICBitmapDitherTypeNone, NULL, 0.0,
        WICBitmapPaletteTypeCustom);
    if (FAILED(hr)) goto done;
    hr = IWICBitmapSource_GetSize((IWICBitmapSource *)converter, &width, &height);
    if (FAILED(hr) || width == 0 || height == 0 || width > 8192 || height > 8192) { hr = E_INVALIDARG; goto done; }
    UINT stride = width * 4, bytes = stride * height;
    pixels = HeapAlloc(GetProcessHeap(), 0, bytes);
    if (!pixels) { hr = E_OUTOFMEMORY; goto done; }
    hr = IWICBitmapSource_CopyPixels((IWICBitmapSource *)converter, NULL, stride, bytes, pixels);
    if (FAILED(hr)) goto done;
    hr = D3D11CreateDevice(NULL, D3D_DRIVER_TYPE_HARDWARE, NULL, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
        NULL, 0, D3D11_SDK_VERSION, &state->image_device, NULL, &state->image_context);
    if (FAILED(hr)) {
        hr = D3D11CreateDevice(NULL, D3D_DRIVER_TYPE_WARP, NULL, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            NULL, 0, D3D11_SDK_VERSION, &state->image_device, NULL, &state->image_context);
    }
    if (FAILED(hr)) goto done;
    hr = ID3D11Device_QueryInterface(state->image_device, &pv_iid_dxgi_device, (void **)&dxgi_device);
    if (FAILED(hr)) goto done;
    hr = IDXGIDevice_GetAdapter(dxgi_device, &adapter);
    if (FAILED(hr)) goto done;
    hr = IDXGIAdapter_GetParent(adapter, &pv_iid_dxgi_factory2, (void **)&dxgi_factory);
    if (FAILED(hr)) goto done;
    DXGI_SWAP_CHAIN_DESC1 description;
    ZeroMemory(&description, sizeof(description));
    description.Width = width; description.Height = height;
    description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    description.SampleDesc.Count = 1;
    description.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    description.BufferCount = 2;
    description.Scaling = DXGI_SCALING_STRETCH;
    description.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
    description.AlphaMode = DXGI_ALPHA_MODE_IGNORE;
    hr = IDXGIFactory2_CreateSwapChainForComposition(dxgi_factory, (IUnknown *)state->image_device,
        &description, NULL, &state->image_swapchain);
    if (FAILED(hr)) goto done;
    hr = IDXGISwapChain1_GetBuffer(state->image_swapchain, 0, &pv_iid_d3d11_texture2d,
        (void **)&backbuffer);
    if (FAILED(hr)) goto done;
    hr = IDXGISwapChain1_GetBuffer(state->image_swapchain, 1, &pv_iid_d3d11_texture2d,
        (void **)&backbuffer1);
    if (FAILED(hr)) goto done;
    ID3D11DeviceContext_UpdateSubresource(state->image_context, (ID3D11Resource *)backbuffer,
        0, NULL, pixels, stride, bytes);
    ID3D11DeviceContext_UpdateSubresource(state->image_context, (ID3D11Resource *)backbuffer1,
        0, NULL, pixels, stride, bytes);
    ID3D11DeviceContext_Flush(state->image_context);
    hr = IDXGISwapChain1_Present(state->image_swapchain, 0, 0);
    if (FAILED(hr)) goto done;
    hr = pv_query(state->compositor, &pv_compositor_interop, &interopObject);
    if (FAILED(hr)) goto done;
    hr = ((PvCreateCompositionSurfaceForSwapChain)interopObject->methods[4])(
        interopObject, (IUnknown *)state->image_swapchain, &state->image_surface);
    if (FAILED(hr)) goto done;
    hr = ICompositor_CreateSurfaceBrushWithSurface((ICompositor *)state->base,
        (ICompositionSurface *)state->image_surface, (ICompositionSurfaceBrush **)&state->image_brush);
    if (FAILED(hr)) goto done;
    ICompositionSurfaceBrush_put_Stretch((ICompositionSurfaceBrush *)state->image_brush, CompositionStretch_UniformToFill);
    ICompositionSurfaceBrush_put_HorizontalAlignmentRatio((ICompositionSurfaceBrush *)state->image_brush, 0.5f);
    ICompositionSurfaceBrush_put_VerticalAlignmentRatio((ICompositionSurfaceBrush *)state->image_brush, 0.5f);
    *result = state->image_brush;
done:
    if (interopObject) pv_release(interopObject);
    if (backbuffer) ID3D11Texture2D_Release(backbuffer);
    if (backbuffer1) ID3D11Texture2D_Release(backbuffer1);
    if (dxgi_factory) IDXGIFactory2_Release(dxgi_factory);
    if (adapter) IDXGIAdapter_Release(adapter);
    if (dxgi_device) IDXGIDevice_Release(dxgi_device);
    if (pixels) HeapFree(GetProcessHeap(), 0, pixels);
    if (converter) IWICFormatConverter_Release(converter);
    if (frame) IWICBitmapFrameDecode_Release(frame);
    if (decoder) IWICBitmapDecoder_Release(decoder);
    if (wic) IWICImagingFactory_Release(wic);
    if (FAILED(hr)) {
        if (state->image_brush) { pv_release(state->image_brush); state->image_brush = NULL; }
        if (state->image_surface) { pv_release(state->image_surface); state->image_surface = NULL; }
        if (state->image_swapchain) { IDXGISwapChain1_Release(state->image_swapchain); state->image_swapchain = NULL; }
        if (state->image_context) { ID3D11DeviceContext_Release(state->image_context); state->image_context = NULL; }
        if (state->image_device) { ID3D11Device_Release(state->image_device); state->image_device = NULL; }
    }
    return hr;
}
static HRESULT backdrop_overlay_create(HWND window, DesktopBackdrop *state) {
    if (!state || !state->base || !state->compositor) return E_INVALIDARG;
    if (state->overlay_window) return S_OK;
    HWND overlay = backdrop_overlay_window(window);
    if (!overlay) return HRESULT_FROM_WIN32(GetLastError());
    HRESULT hr;
    PvCom *interop = NULL, *container_visual = NULL;
    hr = pv_query(state->compositor, &pv_desktop, &interop);
    if (FAILED(hr)) goto failed;
    hr = ((PvCreate)state->base->methods[9])(state->base, &state->overlay_container);
    if (FAILED(hr)) goto failed;
    hr = pv_query(state->overlay_container, &pv_visual, &state->overlay_visual);
    if (FAILED(hr)) goto failed;
    hr = pv_query(state->overlay_container, &pv_container_visual, &container_visual);
    if (FAILED(hr)) goto failed;
    hr = ((PvCreate)container_visual->methods[6])(container_visual, &state->overlay_children);
    if (FAILED(hr)) goto failed;
    hr = ((PvCreate)state->base->methods[22])(state->base, &state->overlay_sprite);
    if (FAILED(hr)) goto failed;
    hr = pv_query(state->overlay_sprite, &pv_visual, &state->overlay_desktop_visual);
    if (FAILED(hr)) goto failed;
    hr = ((PvSetObject)state->overlay_sprite->methods[7])(state->overlay_sprite, state->brush);
    if (FAILED(hr)) goto failed;
    hr = ((PvInsertVisual)state->overlay_children->methods[8])(state->overlay_children, state->overlay_desktop_visual);
    if (FAILED(hr)) goto failed;
    if (state->image_active && state->image_effect) {
        hr = ((PvCreate)state->base->methods[22])(state->base, &state->overlay_image_sprite);
        if (FAILED(hr)) goto failed;
        hr = pv_query(state->overlay_image_sprite, &pv_visual, &state->overlay_image_visual);
        if (FAILED(hr)) goto failed;
        hr = ((PvSetObject)state->overlay_image_sprite->methods[7])(state->overlay_image_sprite, state->image_effect);
        if (FAILED(hr)) goto failed;
        hr = ((PvInsertVisual)state->overlay_children->methods[9])(state->overlay_children, state->overlay_image_visual);
        if (FAILED(hr)) goto failed;
    }
    /* The child is already above the parent's client content in the child
       z-order. Keep the composition target non-topmost, matching the main
       backdrop target; topmost HWND targets are rejected by Neovide's
       DirectComposition swapchain on this runtime. */
    hr = ((PvCreateTarget)interop->methods[3])(interop, overlay, FALSE, &state->overlay_target);
    if (FAILED(hr)) goto failed;
    hr = ((PvSetObject)state->overlay_target->methods[7])(state->overlay_target, state->overlay_visual);
    if (FAILED(hr)) goto failed;
    state->overlay_window = overlay;
    pv_release(container_visual); pv_release(interop);
    return S_OK;
failed:
    pv_release(container_visual); pv_release(interop);
    if (state->overlay_target || state->overlay_container || state->overlay_window)
        backdrop_overlay_destroy(state);
    else if (overlay) DestroyWindow(overlay);
    return hr;
}
static HRESULT backdrop_overlay_size(HWND window, DesktopBackdrop *state) {
    if (!state || !state->overlay_window || !state->overlay_visual) return S_OK;
    RECT rect; GetClientRect(window, &rect);
    int width = rect.right - rect.left, height = rect.bottom - rect.top;
    int bottom = backdrop_value(window, L"PinyinCapsules.SubmitBottom");
    if (width < 1 || height < 1 || bottom <= 0 || bottom >= height) {
        ShowWindow(state->overlay_window, SW_HIDE);
        return S_OK;
    }
    int overlay_height = height - bottom;
    SetWindowPos(state->overlay_window, HWND_TOP, 0, bottom, width, overlay_height,
        SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_SHOWWINDOW);
    backdrop_overlay_region(state->overlay_window, width, overlay_height);
    PvVector2 size = {(float)width, (float)overlay_height};
    HRESULT hr = ((PvSetSize)state->overlay_visual->methods[36])(state->overlay_visual, size);
    if (SUCCEEDED(hr) && state->overlay_desktop_visual)
        hr = ((PvSetSize)state->overlay_desktop_visual->methods[36])(state->overlay_desktop_visual, size);
    if (SUCCEEDED(hr) && state->overlay_image_visual)
        hr = ((PvSetSize)state->overlay_image_visual->methods[36])(state->overlay_image_visual, size);
    return hr;
}
static HRESULT backdrop_overlay_opacity(HWND window, DesktopBackdrop *state) {
    if (!state || !state->overlay_visual) return S_OK;
    int transparency = window ? backdrop_value(window, L"PinyinCapsules.BackgroundTransparency") : 0;
    if (transparency < 0) transparency = 0;
    if (transparency > 100) transparency = 100;
    float image_opacity = state->image_active ? 1.0f - transparency / 100.0f : 0.0f;
    /* Keep the desktop backdrop fully opaque. The image layer above it owns
       the image transparency; fading both layers leaves an alpha hole through
       which sharp desktop text becomes visible. */
    float desktop_opacity = 1.0f;
    HRESULT hr = IVisual_put_Opacity((IVisual *)state->overlay_visual, 1.0f);
    if (SUCCEEDED(hr) && state->overlay_desktop_visual)
        hr = IVisual_put_Opacity((IVisual *)state->overlay_desktop_visual, desktop_opacity);
    if (SUCCEEDED(hr) && state->overlay_image_visual)
        hr = IVisual_put_Opacity((IVisual *)state->overlay_image_visual, image_opacity);
    return hr;
}
static HRESULT backdrop_size(HWND window, DesktopBackdrop *state) {
    RECT rect; GetClientRect(window, &rect);
    float width = (float)(rect.right - rect.left), height = (float)(rect.bottom - rect.top);
    PvVector2 size = {width, height};
    HRESULT hr = ((PvSetSize)state->visual->methods[36])(state->visual, size);
    if (SUCCEEDED(hr) && state->desktop_visual)
        hr = ((PvSetSize)state->desktop_visual->methods[36])(state->desktop_visual, size);
    if (SUCCEEDED(hr) && state->image_visual)
        hr = ((PvSetSize)state->image_visual->methods[36])(state->image_visual, size);
    if (SUCCEEDED(hr)) hr = backdrop_overlay_size(window, state);
    return hr;
}
static HRESULT backdrop_rebuild_effect(DesktopBackdrop *state, float sigma) {
    if (!state || !state->base || !state->input || !state->sprite) return E_INVALIDARG;
    PvString make_string = (PvString)(void *)GetProcAddress(state->runtime, "WindowsCreateString");
    PvDeleteString delete_string = (PvDeleteString)(void *)GetProcAddress(state->runtime, "WindowsDeleteString");
    PvFactory get_factory = (PvFactory)(void *)GetProcAddress(state->runtime, "RoGetActivationFactory");
    if (!make_string || !delete_string || !get_factory) return E_NOTIMPL;
    PvCom *new_factory = NULL, *new_brush = NULL, *new_image_factory = NULL, *new_image_effect = NULL;
    HRESULT hr = gaussian_brush(state->base, state->input, make_string, delete_string,
        get_factory, sigma, &new_factory, &new_brush);
    if (FAILED(hr)) return hr;
    if (state->image_active) {
        hr = gaussian_brush(state->base, state->image_brush, make_string, delete_string,
            get_factory, sigma, &new_image_factory, &new_image_effect);
        if (FAILED(hr)) goto failed;
    }
    hr = ((PvSetObject)state->sprite->methods[7])(state->sprite, new_brush);
    if (FAILED(hr)) goto failed;
    if (state->overlay_sprite) {
        hr = ((PvSetObject)state->overlay_sprite->methods[7])(state->overlay_sprite, new_brush);
        if (FAILED(hr)) goto failed;
    }
    if (state->image_active && state->image_sprite) {
        hr = ((PvSetObject)state->image_sprite->methods[7])(state->image_sprite, new_image_effect);
        if (FAILED(hr)) goto failed;
    }
    if (state->image_active && state->overlay_image_sprite) {
        hr = ((PvSetObject)state->overlay_image_sprite->methods[7])(state->overlay_image_sprite, new_image_effect);
        if (FAILED(hr)) goto failed;
    }
    PvCom *old_brush = state->brush, *old_factory = state->factory;
    PvCom *old_image_effect = state->image_effect, *old_image_factory = state->image_factory;
    state->brush = new_brush; state->factory = new_factory;
    state->image_effect = new_image_effect; state->image_factory = new_image_factory;
    state->sigma = sigma;
    /* The compositor may still consume these objects after the visual source
       is replaced. Release our references, but do not call IClosable::Close:
       Close invalidates an object while a pending DComp commit can still walk
       it and caused the dcomp.dll access violation on theme/image changes. */
    pv_release(old_brush);
    pv_release(old_factory);
    pv_release(old_image_effect);
    pv_release(old_image_factory);
    return S_OK;
failed:
    pv_close(new_image_effect); pv_release(new_image_effect);
    pv_close(new_image_factory); pv_release(new_image_factory);
    pv_close(new_brush); pv_release(new_brush);
    pv_close(new_factory); pv_release(new_factory);
    return hr;
}

static HRESULT backdrop_ensure_image_visual(DesktopBackdrop *state) {
    if (!state || !state->children || !state->image_brush) return E_INVALIDARG;
    if (state->image_sprite) return S_OK;
    HRESULT hr = ((PvCreate)state->base->methods[22])(state->base, &state->image_sprite);
    if (FAILED(hr)) return hr;
    hr = pv_query(state->image_sprite, &pv_visual, &state->image_visual);
    if (SUCCEEDED(hr)) hr = ((PvInsertVisual)state->children->methods[9])(state->children, state->image_visual);
    if (FAILED(hr)) {
        pv_close(state->image_sprite); pv_release(state->image_visual); pv_release(state->image_sprite);
        state->image_visual = NULL; state->image_sprite = NULL;
    }
    return hr;
}

static HRESULT backdrop_replace_image(DesktopBackdrop *state, const wchar_t *path) {
    if (!state) return E_INVALIDARG;
    PvCom *old_brush = state->image_brush, *old_surface = state->image_surface;
    IDXGISwapChain1 *old_swapchain = state->image_swapchain;
    ID3D11DeviceContext *old_context = state->image_context;
    ID3D11Device *old_device = state->image_device;
    PvCom *old_image_effect = state->image_effect, *old_image_factory = state->image_factory;
    int old_active = state->image_active;
    state->image_brush = NULL; state->image_surface = NULL;
    state->image_swapchain = NULL; state->image_context = NULL; state->image_device = NULL;
    state->image_effect = NULL; state->image_factory = NULL;
    state->image_active = 0;
    HRESULT hr = S_OK;
    if (path && path[0]) {
        hr = backdrop_image_brush(state, path, &state->image_brush);
        /* A missing/invalid image means “no image”; preserve the live
           desktop backdrop instead of keeping stale pixels or failing the
           whole editor. */
        if (FAILED(hr)) { hr = S_OK; state->image_active = 0; }
    }
    if (SUCCEEDED(hr) && state->image_brush) {
        state->image_active = 1;
        hr = backdrop_ensure_image_visual(state);
        if (SUCCEEDED(hr)) {
            PvString make_string = (PvString)(void *)GetProcAddress(state->runtime, "WindowsCreateString");
            PvDeleteString delete_string = (PvDeleteString)(void *)GetProcAddress(state->runtime, "WindowsDeleteString");
            PvFactory get_factory = (PvFactory)(void *)GetProcAddress(state->runtime, "RoGetActivationFactory");
            if (!make_string || !delete_string || !get_factory) hr = E_NOTIMPL;
            else hr = gaussian_brush(state->base, state->image_brush, make_string, delete_string,
                get_factory, state->sigma, &state->image_factory, &state->image_effect);
        }
    }
    if (SUCCEEDED(hr)) {
        if (state->image_sprite)
            hr = ((PvSetObject)state->image_sprite->methods[7])(state->image_sprite, state->image_effect);
    }
    if (SUCCEEDED(hr) && state->image_active && state->overlay_window)
        hr = backdrop_overlay_ensure_image_visual(state);
    if (SUCCEEDED(hr)) {
        /* Drop only this thread's references; the visual may still hold the
           old objects until its composition transaction is consumed. */
        pv_release(old_image_effect);
        pv_release(old_image_factory);
        if (old_brush) { pv_release(old_brush); }
        pv_release(old_surface);
        if (old_swapchain) IDXGISwapChain1_Release(old_swapchain);
        if (old_context) ID3D11DeviceContext_Release(old_context);
        if (old_device) ID3D11Device_Release(old_device);
        return S_OK;
    }
    backdrop_release_image_parts(state);
    pv_close(state->image_effect); pv_release(state->image_effect);
    pv_close(state->image_factory); pv_release(state->image_factory);
    state->image_effect = old_image_effect; state->image_factory = old_image_factory;
    state->image_brush = old_brush; state->image_surface = old_surface;
    state->image_swapchain = old_swapchain; state->image_context = old_context; state->image_device = old_device;
    state->image_active = old_active;
    if (state->image_sprite)
        ((PvSetObject)state->image_sprite->methods[7])(state->image_sprite, state->image_effect);
    return hr;
}
static HRESULT backdrop_opacity(HWND window, DesktopBackdrop *state) {
    int transparency = window ? backdrop_value(window, L"PinyinCapsules.BackgroundTransparency") : 0;
    if (transparency < 0) transparency = 0;
    if (transparency > 100) transparency = 100;
    float image_opacity = state->image_active ? 1.0f - transparency / 100.0f : 0.0f;
    /* The desktop source is always the opaque, equally blurred base layer. */
    float desktop_opacity = 1.0f;
    HRESULT hr = IVisual_put_Opacity((IVisual *)state->visual, 1.0f);
    if (SUCCEEDED(hr) && state->desktop_visual)
        hr = IVisual_put_Opacity((IVisual *)state->desktop_visual, desktop_opacity);
    if (SUCCEEDED(hr) && state->image_visual)
        hr = IVisual_put_Opacity((IVisual *)state->image_visual, image_opacity);
    return hr;
}
static HRESULT backdrop_create(HWND window, float sigma, DesktopBackdrop **result) {
    DesktopBackdrop *state = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(DesktopBackdrop));
    PvCom *base = NULL, *extended = NULL, *interop = NULL;
    void *name = NULL;
    HRESULT hr = E_OUTOFMEMORY;
    if (!state) return hr;
    state->runtime = LoadLibraryW(L"combase.dll");
    state->messaging = LoadLibraryW(L"CoreMessaging.dll");
    if (!state->runtime || !state->messaging) { hr = HRESULT_FROM_WIN32(GetLastError()); goto done; }
    PvRoInitialize initialize = (PvRoInitialize)(void *)GetProcAddress(state->runtime, "RoInitialize");
    state->uninitialize = (PvRoUninitialize)(void *)GetProcAddress(state->runtime, "RoUninitialize");
    PvString make_string = (PvString)(void *)GetProcAddress(state->runtime, "WindowsCreateString");
    PvDeleteString delete_string = (PvDeleteString)(void *)GetProcAddress(state->runtime, "WindowsDeleteString");
    PvFactory get_factory = (PvFactory)(void *)GetProcAddress(state->runtime, "RoGetActivationFactory");
    PvActivate activate = (PvActivate)(void *)GetProcAddress(state->runtime, "RoActivateInstance");
    PvCreateQueue create_queue = (PvCreateQueue)(void *)GetProcAddress(state->messaging, "CreateDispatcherQueueController");
    if (!initialize || !state->uninitialize || !make_string || !delete_string || !activate || !get_factory || !create_queue) {
        hr = E_NOTIMPL; goto done;
    }
    hr = initialize(0); // Neovide's Winit owner thread is STA.
    if (FAILED(hr)) goto done;
    state->initialized = 1;
    PvQueueOptions options = {sizeof(PvQueueOptions), 2, 2}; // current thread, STA
    hr = create_queue(options, &state->queue);
    // An existing current-thread dispatcher belongs to its original owner.
    if (FAILED(hr) && hr != HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS)) goto done;
    const wchar_t *class_name = L"Windows.UI.Composition.Compositor";
    hr = make_string(class_name, (UINT)lstrlenW(class_name), &name);
    if (FAILED(hr)) goto done;
    hr = activate(name, &state->compositor);
    delete_string(name); name = NULL;
    if (FAILED(hr)) goto done;
    hr = pv_query(state->compositor, &pv_compositor, &base); if (FAILED(hr)) goto done;
    state->base = base; base = NULL;
    hr = pv_query(state->compositor, &pv_compositor2, &extended); if (FAILED(hr)) goto done;
    hr = pv_query(state->compositor, &pv_desktop, &interop); if (FAILED(hr)) goto done;
    /* Always retain the live desktop backdrop. An image is a second layer;
       it must never replace the desktop source, otherwise the transparent
       parts reveal the sharp application behind the window. */
    hr = ((PvCreate)extended->methods[8])(extended, &state->input);
    if (FAILED(hr)) goto done;
    state->sigma = sigma;
    hr = gaussian_brush(state->base, state->input, make_string, delete_string, get_factory, sigma, &state->factory, &state->brush); if (FAILED(hr)) goto done;
    hr = ((PvCreate)state->base->methods[22])(state->base, &state->sprite); if (FAILED(hr)) goto done;
    hr = pv_query(state->sprite, &pv_visual, &state->desktop_visual); if (FAILED(hr)) goto done;
    hr = ((PvSetObject)state->sprite->methods[7])(state->sprite, state->brush); if (FAILED(hr)) goto done;
    hr = ((PvCreate)state->base->methods[9])(state->base, &state->container); if (FAILED(hr)) goto done;
    hr = pv_query(state->container, &pv_visual, &state->visual); if (FAILED(hr)) goto done;
    PvCom *container_visual = NULL;
    hr = pv_query(state->container, &pv_container_visual, &container_visual); if (FAILED(hr)) goto done;
    hr = ((PvCreate)container_visual->methods[6])(container_visual, &state->children);
    pv_release(container_visual); if (FAILED(hr)) goto done;
    hr = ((PvInsertVisual)state->children->methods[8])(state->children, state->desktop_visual); if (FAILED(hr)) goto done;
    const wchar_t *image = backdrop_image_path(window);
    if (image && image[0] && SUCCEEDED(backdrop_image_brush(state, image, &state->image_brush))) {
        state->image_active = 1;
        hr = backdrop_ensure_image_visual(state);
        if (SUCCEEDED(hr)) {
            hr = gaussian_brush(state->base, state->image_brush, make_string, delete_string,
                get_factory, sigma, &state->image_factory, &state->image_effect);
            if (SUCCEEDED(hr)) hr = ((PvSetObject)state->image_sprite->methods[7])(state->image_sprite, state->image_effect);
        }
        if (FAILED(hr)) {
            backdrop_release_image_effect(state);
            backdrop_release_image_parts(state);
            hr = S_OK;
        }
    }
    // Neovide already owns the topmost=true DirectComposition target. Put the
    // container with the equally blurred desktop and image layers BELOW it,
    // on the same HWND. No color brush, extra HWND, global alpha, system
    // material or screen capture is used.
    hr = ((PvCreateTarget)interop->methods[3])(interop, window, FALSE, &state->desktop_target);
    if (FAILED(hr)) goto done;
    hr = pv_query(state->desktop_target, &pv_target, &state->target); if (FAILED(hr)) goto done;
    /* A second composition target is intentionally disabled. DirectComposition
       rejects a second HWND target on this Neovide swapchain and can terminate
       dcomp.dll; the client remainder is corrected by geometry below. */
    if (0 && SUCCEEDED(hr)) {
        HRESULT overlay_hr = backdrop_overlay_create(window, state);
        if (FAILED(overlay_hr)) state->overlay_window = NULL;
    }
    hr = backdrop_size(window, state);
    if (SUCCEEDED(hr)) hr = backdrop_opacity(window, state);
    if (SUCCEEDED(hr)) hr = backdrop_overlay_opacity(window, state);
done:
    pv_release(base); pv_release(extended); pv_release(interop);
    if (FAILED(hr)) backdrop_dispose(state);
    else *result = state;
    return hr;
}
static void backdrop_apply(HWND window, int requested) {
    DesktopBackdrop *state = GetPropW(window, L"PinyinCapsules.BackdropObjects");
    HRESULT hr = S_OK;
    int off = 0, none = 1;
    float sigma = (float)backdrop_value(window, L"PinyinCapsules.BlurRadius");
    if (sigma < 0.0f) sigma = 0.0f;
    if (sigma > 24.0f) sigma = 24.0f;
    // Keep incompatible HostBackdrop and system materials disabled.
    DwmSetWindowAttribute(window, 17, &off, sizeof(off));
    DwmSetWindowAttribute(window, 38, &none, sizeof(none));
    // Rebuild the immutable effect exactly once when the release of the
    // blur slider changes its radius. Resize continues to reuse the visual.
    float effective_sigma = requested ? sigma : 0.0f;
    if (state && (state->sigma != effective_sigma))
        hr = backdrop_rebuild_effect(state, effective_sigma);
    const wchar_t *image = backdrop_image_path(window);
    int wants_image = image && image[0];
    if ((requested || wants_image) && !state) {
        /* An image remains visible when blur is disabled. In that case an
           identity Gaussian (sigma 0) keeps one composition path and still
           obeys the same opacity and resize semantics. */
        hr = backdrop_create(window, effective_sigma, &state);
        if (SUCCEEDED(hr)) SetPropW(window, L"PinyinCapsules.BackdropObjects", state);
    }
    if (state && SUCCEEDED(hr)) {
        hr = backdrop_size(window, state);
        if (SUCCEEDED(hr)) hr = backdrop_opacity(window, state);
        if (SUCCEEDED(hr)) hr = backdrop_overlay_opacity(window, state);
        if (SUCCEEDED(hr)) hr = ((PvSetObject)state->target->methods[7])(state->target,
            (requested || state->image_active) ? state->visual : NULL);
        if (SUCCEEDED(hr) && state->overlay_target)
            hr = ((PvSetObject)state->overlay_target->methods[7])(state->overlay_target,
                (requested || state->image_active) ? state->overlay_visual : NULL);
        state->enabled = requested && SUCCEEDED(hr);
        if (FAILED(hr)) {
            ((PvSetObject)state->target->methods[7])(state->target, NULL);
            if (state->overlay_target) ((PvSetObject)state->overlay_target->methods[7])(state->overlay_target, NULL);
        }
    }
    SetPropW(window, L"PinyinCapsules.BackdropResult", (HANDLE)(INT_PTR)hr);
    SetPropW(window, L"PinyinCapsules.BlurRequested", (HANDLE)(INT_PTR)(requested != 0));
    SetPropW(window, L"PinyinCapsules.BlurAvailable", (HANDLE)(INT_PTR)(state != NULL && SUCCEEDED(hr)));
    SetPropW(window, L"PinyinCapsules.DesktopBlur", (HANDLE)(INT_PTR)(state && state->enabled));
    SetPropW(window, L"PinyinCapsules.ImageActive", (HANDLE)(INT_PTR)(state && state->image_active && SUCCEEDED(hr)));
}
static void backdrop_resize(HWND window) {
    DesktopBackdrop *state = GetPropW(window, L"PinyinCapsules.BackdropObjects");
    if (state) backdrop_size(window, state);
}
static void backdrop_destroy(HWND window) {
    backdrop_dispose(RemovePropW(window, L"PinyinCapsules.BackdropObjects"));
    RemovePropW(window, L"PinyinCapsules.BackdropResult");
    RemovePropW(window, L"PinyinCapsules.BlurRequested");
    RemovePropW(window, L"PinyinCapsules.BlurAvailable");
    RemovePropW(window, L"PinyinCapsules.DesktopBlur");
    RemovePropW(window, L"PinyinCapsules.ImageActive");
}
