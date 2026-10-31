const std = @import("std");
const abi = @import("abi");

test "a flat function fails with an error and answers with its out parameter" {
    const db = try abi.checked.sqlite3_open(":memory:");
    try abi.checked.sqlite3_close(db);
}
