namespace Probe.Native;

public unsafe partial struct ke_probe_batch
{
    [NativeTypeName("const ke_probe_item *")]
    public ProbeItem* items;

    [NativeTypeName("uint32_t")]
    public uint item_count;
}
