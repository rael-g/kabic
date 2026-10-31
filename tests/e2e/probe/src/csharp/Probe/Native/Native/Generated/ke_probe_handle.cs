using global::Probe;

namespace Probe.Native;

public unsafe partial struct ke_probe_handle
{
    public ke_probe* @ref;

    [NativeTypeName("void (*)(ke_probe *)")]
    public delegate* unmanaged[Cdecl]<ke_probe*, void> destroy;
}
