namespace Probe.Shared { internal static class Anchor { } }

namespace Probe
{
    public sealed class NativeOwner(System.Action teardown) : System.Runtime.InteropServices.SafeHandle(new System.IntPtr(1), true)
    {
        public override bool IsInvalid => false;
        protected override bool ReleaseHandle()
        {
            teardown();
            return true;
        }
    }

    public static class Results
    {
        public static void ThrowIfFailed(global::Probe.Result code, string call)
        {
            if (code != global::Probe.Result.Ok) throw new InvalidOperationException(call);
        }

        public static Exception FromCode(global::Probe.Result code, string call) => new InvalidOperationException(call);

        public static global::Probe.Result ToCode(Exception exception) => global::Probe.Result.General;
    }
}
