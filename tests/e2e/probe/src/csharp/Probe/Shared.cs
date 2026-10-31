namespace Probe.Shared { internal static class Anchor { } }

namespace Probe
{
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
