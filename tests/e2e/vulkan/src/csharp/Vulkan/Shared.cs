namespace Vulkan.Shared { internal static class Anchor { } }

namespace Vulkan
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

    public sealed class VulkanException(global::Vulkan.Native.VkResult code, string call) : Exception($"{call}: {code}")
    {
        public global::Vulkan.Native.VkResult Code { get; } = code;
    }

    public static class Results
    {
        public static void ThrowIfFailed(global::Vulkan.Native.VkResult code, string call)
        {
            if (code < 0) throw new VulkanException(code, call);
        }

        public static Exception FromCode(global::Vulkan.Native.VkResult code, string call) => new VulkanException(code, call);

        public static global::Vulkan.Native.VkResult ToCode(Exception exception) => global::Vulkan.Native.VkResult.VkSuccess;
    }
}
