const std = @import("std");
const abi = @import("abi");

test "a flat function fails with an error and answers with its out parameter" {
    const db = try abi.checked.open(":memory:");
    try abi.checked.close(db);
}
