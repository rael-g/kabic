namespace Probe.Native;

public partial struct ProbeItem
{
    [NativeTypeName("uint32_t")]
    public uint Id;

    public float Weight;

    [NativeTypeName("ke_probe_kind")]
    public ProbeKind Kind;
}
