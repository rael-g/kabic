using Probe.Native;

namespace Other.Native;

public partial struct OtherPair
{
    [NativeTypeName("ke_probe_item")]
    public Probe.Native.ProbeItem First;

    [NativeTypeName("ke_probe_kind")]
    public Probe.Native.ProbeKind Kind;
}
