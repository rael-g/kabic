using global::Probe;

namespace Probe.Native;

public unsafe partial struct ke_probe_label
{
    [NativeTypeName("const uint32_t *")]
    public uint* ids;

    [NativeTypeName("uint32_t")]
    public uint id_count;
}
