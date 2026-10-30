namespace Probe.Native;

public unsafe partial struct ke_probe
{
    public void* handle;

    [NativeTypeName("uint32_t (*)(struct ke_probe *)")]
    public delegate* unmanaged[Cdecl]<ke_probe*, uint> count;

    [NativeTypeName("float (*)(struct ke_probe *, uint32_t)")]
    public delegate* unmanaged[Cdecl]<ke_probe*, uint, float> weight_of;

    [NativeTypeName("void (*)(struct ke_probe *, const ke_probe_item *, uint32_t)")]
    public delegate* unmanaged[Cdecl]<ke_probe*, ProbeItem*, uint, void> fill;

    [NativeTypeName("ke_result (*)(struct ke_probe *, uint32_t, float *)")]
    public delegate* unmanaged[Cdecl]<ke_probe*, uint, float*, Result> read;

    [NativeTypeName("ke_result (*)(struct ke_probe *, ke_probe_visit_fn, void *)")]
    public delegate* unmanaged[Cdecl]<ke_probe*, delegate* unmanaged[Cdecl]<void*, uint, void>, void*, Result> each;
}
