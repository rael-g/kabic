namespace Probe.Native;

public unsafe partial struct ke_probe
{
    public void* handle;

    [NativeTypeName("uint32_t (*)(struct ke_probe *)")]
    public delegate* unmanaged[Cdecl]<ke_probe*, uint> count;

    [NativeTypeName("float (*)(struct ke_probe *, uint32_t)")]
    public delegate* unmanaged[Cdecl]<ke_probe*, uint, float> weight_of;

    [NativeTypeName("void (*)(struct ke_probe *, const ke_probe_item *, uint32_t)")]
    public delegate* unmanaged[Cdecl]<ke_probe*, ke_probe_item*, uint, void> fill;
}
