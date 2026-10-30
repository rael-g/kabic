var version = Vulkan.Vulkan.EnumerateInstanceVersion();
Console.WriteLine($"vulkan {version >> 22}.{(version >> 12) & 0x3ff}.{version & 0xfff}");
