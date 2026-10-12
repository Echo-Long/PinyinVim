// Immutable WinRT IGraphicsEffect and native D2D effect description.
// ABI: Microsoft windows-rs 0.58.0 / windows.graphics.effects.interop.h.
#include <objbase.h>
#include <stddef.h>
#include <string.h>
typedef HRESULT (WINAPI *PvFactory)(void *, const GUID *, PvCom **);
typedef HRESULT (WINAPI *PvNamedObject)(PvCom *, void *, PvCom **);
typedef HRESULT (WINAPI *PvSetNamedObject)(PvCom *, void *, PvCom *);
typedef HRESULT (WINAPI *PvCreateEffect)(PvCom *, PvCom *, PvCom **);
typedef HRESULT (WINAPI *PvSingle)(PvCom *, float, PvCom **);
typedef HRESULT (WINAPI *PvUInt)(PvCom *, UINT, PvCom **);
static const GUID pv_unknown = {0,0,0,{0xc0,0,0,0,0,0,0,0x46}};
static const GUID pv_inspectable = {0xaf86e2e0,0xb12d,0x4c6a,{0x9c,0x5a,0xd7,0xaa,0x65,0x10,0x1e,0x90}};
static const GUID pv_graphics_effect = {0xcb51c0ce,0x8fe6,0x4636,{0xb2,0x02,0x86,0x1f,0xaa,0x07,0xd8,0xf3}};
static const GUID pv_effect_source = {0x2d8f9ddc,0x4339,0x4eb9,{0x92,0x16,0xf9,0xde,0xb7,0x56,0x58,0xa2}};
static const GUID pv_effect_interop = {0x2fc57384,0xa068,0x44d7,{0xa3,0x31,0x30,0x98,0x2f,0xcf,0x71,0x77}};
static const GUID pv_gaussian = {0x1feb6d69,0x2fe6,0x4ac9,{0x8c,0x58,0x1d,0x7f,0x93,0xe7,0xa6,0xa5}};
static const GUID pv_property_statics = {0x629bdbc8,0xd932,0x4ff4,{0x96,0xb9,0x8d,0x96,0xc5,0xc1,0xe8,0x58}};
static const GUID pv_property = {0x4bd682dd,0x7554,0x40e9,{0x9a,0x9b,0x82,0x65,0x4e,0xde,0x7e,0x62}};
static const GUID pv_source_factory = {0xb3d9f276,0xaba3,0x4724,{0xac,0xf3,0xd0,0x39,0x74,0x64,0xdb,0x1c}};
typedef struct {
    PvCom graphics, interop;
    LONG references;
    PvCom *source, *properties[3];
} PvGaussianEffect;
static ULONG WINAPI effect_add(PvGaussianEffect *self) { return InterlockedIncrement(&self->references); }
static ULONG WINAPI effect_release(PvGaussianEffect *self) {
    ULONG references = InterlockedDecrement(&self->references);
    if (!references) {
        pv_release(self->source);
        for (int i = 0; i < 3; ++i) pv_release(self->properties[i]);
        HeapFree(GetProcessHeap(), 0, self);
    }
    return references;
}
static HRESULT WINAPI effect_query(PvGaussianEffect *self, const GUID *id, PvCom **result) {
    if (!result) return E_POINTER;
    *result = NULL;
    if (!memcmp(id, &pv_effect_interop, sizeof(GUID))) *result = &self->interop;
    else if (!memcmp(id, &pv_unknown, sizeof(GUID)) || !memcmp(id, &pv_inspectable, sizeof(GUID)) ||
        !memcmp(id, &pv_graphics_effect, sizeof(GUID)) || !memcmp(id, &pv_effect_source, sizeof(GUID)))
        *result = &self->graphics;
    else return E_NOINTERFACE;
    effect_add(self); return S_OK;
}
static HRESULT WINAPI effect_iids(PvGaussianEffect *self, ULONG *count, GUID **ids) {
    (void)self;
    if (!count || !ids) return E_POINTER;
    *count = 0; *ids = CoTaskMemAlloc(2 * sizeof(GUID));
    if (!*ids) return E_OUTOFMEMORY;
    (*ids)[0] = pv_graphics_effect; (*ids)[1] = pv_effect_source; *count = 2; return S_OK;
}
static HRESULT WINAPI effect_name(PvGaussianEffect *self, void **name) {
    (void)self; if (!name) return E_POINTER; *name = NULL; return S_OK;
}
static HRESULT WINAPI effect_trust(PvGaussianEffect *self, int *trust) {
    (void)self; if (!trust) return E_POINTER; *trust = 0; return S_OK;
}
static HRESULT WINAPI effect_set_name(PvGaussianEffect *self, void *name) { (void)self; (void)name; return S_OK; }
static PvGaussianEffect *effect_owner(PvCom *interop) {
    return (PvGaussianEffect *)((BYTE *)interop - offsetof(PvGaussianEffect, interop));
}
static HRESULT WINAPI effect_interop_query(PvCom *self, const GUID *id, PvCom **result) { return effect_query(effect_owner(self), id, result); }
static ULONG WINAPI effect_interop_add(PvCom *self) { return effect_add(effect_owner(self)); }
static ULONG WINAPI effect_interop_release(PvCom *self) { return effect_release(effect_owner(self)); }
static HRESULT WINAPI effect_id(PvCom *self, GUID *id) { (void)self; if (!id) return E_POINTER; *id = pv_gaussian; return S_OK; }
static HRESULT WINAPI effect_mapping(PvCom *self, PCWSTR name, UINT *index, UINT *mapping) {
    (void)self; (void)name; if (index) *index = 0; if (mapping) *mapping = 0; return E_INVALIDARG;
}
static HRESULT WINAPI effect_property_count(PvCom *self, UINT *count) { (void)self; if (!count) return E_POINTER; *count = 3; return S_OK; }
static HRESULT WINAPI effect_property(PvCom *self, UINT index, PvCom **property) {
    if (!property) return E_POINTER;
    *property = NULL;
    if (index >= 3) return (HRESULT)0x8000000b;
    return pv_query(effect_owner(self)->properties[index], &pv_property, property);
}
static HRESULT WINAPI effect_source(PvCom *self, UINT index, PvCom **source) {
    if (!source) return E_POINTER;
    *source = NULL;
    if (index) return (HRESULT)0x8000000b;
    return pv_query(effect_owner(self)->source, &pv_effect_source, source);
}
static HRESULT WINAPI effect_source_count(PvCom *self, UINT *count) { (void)self; if (!count) return E_POINTER; *count = 1; return S_OK; }
static void *effect_graphics_methods[] = {effect_query, effect_add, effect_release, effect_iids, effect_name, effect_trust, effect_name, effect_set_name};
static void *effect_interop_methods[] = {effect_interop_query, effect_interop_add, effect_interop_release, effect_id, effect_mapping, effect_property_count, effect_property, effect_source, effect_source_count};

static HRESULT gaussian_brush(PvCom *base, PvCom *input, PvString make_string, PvDeleteString delete_string,
    PvFactory get_factory, float sigma, PvCom **factory, PvCom **brush) {
    PvGaussianEffect *effect = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(PvGaussianEffect));
    PvCom *source_factory = NULL, *property_factory = NULL, *effect_brush = NULL;
    void *name = NULL, *parameter = NULL;
    HRESULT hr = E_OUTOFMEMORY;
    if (!effect) return hr;
    effect->graphics.methods = effect_graphics_methods; effect->interop.methods = effect_interop_methods; effect->references = 1;
    const wchar_t *class_name = L"Windows.UI.Composition.CompositionEffectSourceParameter";
    hr = make_string(class_name, (UINT)lstrlenW(class_name), &name); if (FAILED(hr)) goto done;
    hr = get_factory(name, &pv_source_factory, &source_factory); delete_string(name); name = NULL; if (FAILED(hr)) goto done;
    hr = make_string(L"background", 10, &parameter); if (FAILED(hr)) goto done;
    hr = ((PvNamedObject)source_factory->methods[6])(source_factory, parameter, &effect->source); if (FAILED(hr)) goto done;
    class_name = L"Windows.Foundation.PropertyValue";
    hr = make_string(class_name, (UINT)lstrlenW(class_name), &name); if (FAILED(hr)) goto done;
    hr = get_factory(name, &pv_property_statics, &property_factory); delete_string(name); name = NULL; if (FAILED(hr)) goto done;
    for (int i = 0; i < 3; ++i) {
        PvCom *boxed = NULL;
        // Native PropertyValue is required by IGraphicsEffectD2D1Interop.
        if (!i) hr = ((PvSingle)property_factory->methods[14])(property_factory, sigma, &boxed);
        else hr = ((PvUInt)property_factory->methods[11])(property_factory, 1, &boxed); // balanced, hard border
        if (SUCCEEDED(hr)) hr = pv_query(boxed, &pv_property, &effect->properties[i]);
        pv_release(boxed); if (FAILED(hr)) goto done;
    }
    hr = ((PvCreateEffect)base->methods[11])(base, &effect->graphics, factory); if (FAILED(hr)) goto done;
    hr = ((PvCreate)(*factory)->methods[6])(*factory, &effect_brush); if (FAILED(hr)) goto done;
    hr = ((PvSetNamedObject)effect_brush->methods[7])(effect_brush, parameter, input); if (FAILED(hr)) goto done;
    hr = pv_query(effect_brush, &pv_brush, brush);
done:
    if (name) delete_string(name);
    if (parameter) delete_string(parameter);
    effect_release(effect); pv_release(source_factory); pv_release(property_factory); pv_release(effect_brush);
    return hr;
}
