const std = @import("std");
const abi = @import("abi");

comptime {
    std.debug.assert(@sizeOf(abi.c.StdVideoH264SpsVuiFlags) == 4);
    std.debug.assert(@sizeOf(abi.c.VkAccelerationStructureInstanceKHR) == 64);
}

test "every declaration compiles" {
    @setEvalBranchQuota(100_000_000);
    @import("std").testing.refAllDecls(abi);
    inline for (@typeInfo(abi).@"struct".decls) |d| {
        const value = @field(abi, d.name);
        if (@TypeOf(value) == type and @typeInfo(value) == .@"struct") @import("std").testing.refAllDecls(value);
    }
}
