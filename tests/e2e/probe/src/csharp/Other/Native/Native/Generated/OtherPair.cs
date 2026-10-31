using global::Other;

using global::Probe;

namespace Other.Native;

public partial struct OtherPair
{
    [NativeTypeName("ke_probe_item")]
    public global::Probe.ProbeItem First;

    [NativeTypeName("ke_probe_kind")]
    public global::Probe.ProbeKind Kind;
}
