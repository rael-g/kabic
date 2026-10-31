const std = @import("std");
const abi = @import("abi");

const State = struct {
    api: abi.c.ke_probe = abi.Probe.implement(State, "api"),
    weight: f32 = 0,
    filled: u32 = 0,

    pub fn count(self: *State) u32 { return self.filled; }
    pub fn weightOf(self: *State, id: u32) f32 { _ = id; return self.weight; }
    pub fn fill(self: *State, items: []const abi.ProbeItem) void {
        self.filled = @intCast(items.len);
        self.weight = items[0].weight;
    }
    pub fn read(self: *State, id: u32) abi.Error!f32 {
        if (id >= self.filled) return error.ResultGeneral;
        return self.weight;
    }
    pub fn each(self: *State, visit: abi.c.ke_probe_visit_fn, ctx: ?*anyopaque) abi.Error!void {
        visit.?(ctx, self.filled);
    }
    pub fn batch(self: *State) ?*const abi.c.ke_probe_batch { _ = self; return null; }
    pub fn tag(self: *State, label: ?*const abi.c.ke_probe_label) void { _ = self; _ = label; }
    pub fn load(self: *State, id: u32, done: abi.c.ke_probe_done_fn, ctx: ?*anyopaque) void { _ = self; done.?(ctx, 0, id); }
    pub fn countOf(self: *State, item: ?*const abi.c.ke_probe_item) u32 { _ = item; return self.filled; }
    pub fn bind(self: *State, id: u32, instance: ?*anyopaque) abi.Error!void { _ = self; _ = id; _ = instance; }
    pub fn unbind(self: *State, id: u32) void { _ = self; _ = id; }
};

test "a Zig type implements the vtable and the same layer calls it" {
    var state = State{};
    const probe = abi.Probe{ .ptr = &state.api };
    const items = [_]abi.ProbeItem{.{ .id = 0, .weight = 2.5 }};
    probe.fill(&items);
    try std.testing.expectEqual(@as(u32, 1), probe.count());
    try std.testing.expectEqual(@as(f32, 2.5), try probe.read(0));
    try std.testing.expectError(error.ResultGeneral, probe.read(9));
}
