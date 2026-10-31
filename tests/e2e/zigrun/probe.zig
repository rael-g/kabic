const std = @import("std");
const abi = @import("abi");

test "an owner is an object that creates, calls and fails with named errors" {
    var probe = try abi.Probe.create(4);
    defer probe.deinit();
    const items = [_]abi.ProbeItem{.{ .id = 0, .weight = 2.5 }};
    probe.fill(&items);
    try std.testing.expectEqual(@as(u32, 1), probe.count());
    try std.testing.expectEqual(@as(f32, 2.5), try probe.read(0));
    try std.testing.expectError(error.ResultGeneral, probe.read(99));
}

test "every declaration compiles" {
    @import("std").testing.refAllDecls(abi);
    inline for (@typeInfo(abi).@"struct".decls) |d| {
        const value = @field(abi, d.name);
        if (@TypeOf(value) == type and @typeInfo(value) == .@"struct") @import("std").testing.refAllDecls(value);
    }
}
