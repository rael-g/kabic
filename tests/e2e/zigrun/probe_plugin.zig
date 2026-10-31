const std = @import("std");
const abi = @import("abi");
const c = abi.c;

const State = struct {
    api: c.ke_probe = abi.Probe.implement(State, "api"),
    stored: [4]c.ke_probe_item = undefined,
    filled: u32 = 0,
    current: c.ke_probe_batch = .{},
    bound: ?*anyopaque = null,

    pub fn count(self: *State) u32 {
        return self.filled;
    }
    pub fn weightOf(self: *State, id: u32) f32 {
        return self.stored[id].weight;
    }
    pub fn fill(self: *State, items: []const abi.ProbeItem) void {
        self.filled = @intCast(items.len);
        @memcpy(self.stored[0..items.len], items);
    }
    pub fn read(self: *State, id: u32) abi.Error!f32 {
        if (id >= self.filled) return error.ResultGeneral;
        return self.stored[id].weight;
    }
    pub fn each(self: *State, visit: c.ke_probe_visit_fn, ctx: ?*anyopaque) abi.Error!void {
        for (self.stored[0..self.filled]) |item| visit.?(ctx, item.id);
    }
    pub fn batch(self: *State) ?*const c.ke_probe_batch {
        self.current.items = &self.stored;
        self.current.item_count = self.filled;
        return &self.current;
    }
    pub fn tag(_: *State, _: ?*const c.ke_probe_label) void {}
    pub fn load(_: *State, id: u32, done: c.ke_probe_done_fn, ctx: ?*anyopaque) void {
        const thread = std.Thread.spawn(.{}, finish, .{ done, ctx, id }) catch return;
        thread.detach();
    }
    fn finish(done: c.ke_probe_done_fn, ctx: ?*anyopaque, id: u32) void {
        done.?(ctx, c.KE_OK, id + 1);
    }
    pub fn countOf(self: *State, item: ?*const c.ke_probe_item) u32 {
        return if (item) |given| given.id else self.filled;
    }
    pub fn bind(self: *State, _: u32, instance: ?*anyopaque) abi.Error!void {
        self.bound = instance;
    }
    pub fn unbind(self: *State, _: u32) void {
        self.bound = null;
    }
};

fn destroy(self: ?*c.ke_probe) callconv(.c) void {
    const state: *State = @alignCast(@fieldParentPtr("api", self.?));
    std.heap.c_allocator.destroy(state);
}

export fn ke_probe_create(_: u32, out: ?*c.ke_probe_handle) c.ke_result {
    const state = std.heap.c_allocator.create(State) catch return abi.resultOf(error.Failed);
    state.* = .{};
    out.?.* = .{ .ref = &state.api, .destroy = &destroy };
    return c.KE_OK;
}

export fn ke_probe_extra_create(capacity: u32, out: ?*c.ke_probe_handle) c.ke_result {
    if (capacity == 0) return abi.resultOf(error.ResultGeneral);
    return ke_probe_create(capacity, out);
}
