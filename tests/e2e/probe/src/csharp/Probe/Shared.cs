namespace Probe.Shared { internal static class Anchor { } }

namespace Probe
{
    public static class Results
    {
        public static void ThrowIfFailed(global::Probe.Native.Result code, string call)
        {
            if (code != global::Probe.Native.Result.Ok) throw new InvalidOperationException(call);
        }

        public static Exception FromCode(global::Probe.Native.Result code, string call) => new InvalidOperationException(call);

        public static global::Probe.Native.Result ToCode(Exception exception) => global::Probe.Native.Result.General;
    }
}
