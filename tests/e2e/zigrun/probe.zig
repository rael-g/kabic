const std = @import("std");
const abi = @import("abi");

test "a slot fails with an error and answers with its out parameter" {
    const handle = try abi.checked.ke_probe_create(4);
    const probe = handle.ref.?;
    defer handle.destroy.?(probe);
    const items = [_]abi.ke_probe_item{.{ .id = 0, .weight = 2.5 }};
    probe.fill(&items, 1);
    try std.testing.expectEqual(@as(u32, 1), probe.count());
    try std.testing.expectEqual(@as(f32, 2.5), try probe.read(0));
    try std.testing.expectError(error.Failed, probe.read(99));
}
