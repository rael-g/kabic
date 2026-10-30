using Probe.Native;

namespace Other.Native;

public unsafe partial struct ke_other_handle
{
    public ke_other* @ref;

    [NativeTypeName("void (*)(ke_other *)")]
    public delegate* unmanaged[Cdecl]<ke_other*, void> destroy;
}
