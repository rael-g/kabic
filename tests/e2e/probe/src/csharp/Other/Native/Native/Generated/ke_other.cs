using Probe.Native;

namespace Other.Native;

public unsafe partial struct ke_other
{
    public void* handle;

    [NativeTypeName("ke_probe_kind (*)(struct ke_other *, const ke_probe_item *)")]
    public delegate* unmanaged[Cdecl]<ke_other*, Probe.Native.ProbeItem*, Probe.Native.ProbeKind> kind_of;

    [NativeTypeName("ke_other_pair (*)(struct ke_other *)")]
    public delegate* unmanaged[Cdecl]<ke_other*, OtherPair> pair;
}
