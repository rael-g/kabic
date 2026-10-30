using System.Runtime.InteropServices;

namespace Probe.Native;

public static unsafe partial class NativeMethods
{
    [DllImport("ke_probe", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ke_probe_create", ExactSpelling = true)]
    [return: NativeTypeName("ke_result")]
    public static extern Result probe_create([NativeTypeName("uint32_t")] uint capacity, ke_probe_handle* out_handle);
}
