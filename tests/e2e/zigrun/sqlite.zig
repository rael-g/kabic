const abi = @import("abi");

test "an opaque handle is an object with the functions that take it" {
    var db = try abi.Sqlite3.open(":memory:");
    _ = abi.errmsg;
    try db.close();
}

test "every declaration compiles" {
    @import("std").testing.refAllDecls(abi);
    inline for (@typeInfo(abi).@"struct".decls) |d| {
        const value = @field(abi, d.name);
        if (@TypeOf(value) == type and @typeInfo(value) == .@"struct") @import("std").testing.refAllDecls(value);
    }
}
