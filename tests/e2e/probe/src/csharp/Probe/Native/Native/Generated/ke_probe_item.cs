namespace Probe.Native;

public partial struct ke_probe_item
{
    [NativeTypeName("uint32_t")]
    public uint id;

    public float weight;

    public ke_probe_kind kind;
}
