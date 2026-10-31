const std = @import("std");
const abi = @import("abi");

test "bit-fields take the storage C gives them" {
    try std.testing.expectEqual(@as(usize, 4), @sizeOf(abi.StdVideoH264SpsVuiFlags));
    try std.testing.expectEqual(@as(usize, 64), @sizeOf(abi.VkAccelerationStructureInstanceKHR));
}
