const std = @import("std");
const abi = @import("abi");

test "a slot fails with an error and answers with its out parameter" {
    const handle = try abi.checked.probeCreate(4);
    const probe = handle.ref.?;
    defer handle.deinit();
    const items = [_]abi.ProbeItem{.{ .id = 0, .weight = 2.5 }};
    probe.fill(&items);
    try std.testing.expectEqual(@as(u32, 1), probe.count());
    try std.testing.expectEqual(@as(f32, 2.5), try probe.read(0));
    try std.testing.expectError(error.KE_RESULT_GENERAL, probe.read(99));
}
