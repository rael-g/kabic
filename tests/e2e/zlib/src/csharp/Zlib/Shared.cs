namespace Zlib.Shared { internal static class Anchor { } }

namespace Zlib
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

    public sealed class ZlibException(int code, string call) : Exception($"{call}: {code}");

    public static class Results
    {
        public static void ThrowIfFailed(int code, string call)
        {
            if (code != 0) throw new ZlibException(code, call);
        }

        public static Exception FromCode(int code, string call) => new ZlibException(code, call);

        public static int ToCode(Exception exception) => 1;
    }
}
