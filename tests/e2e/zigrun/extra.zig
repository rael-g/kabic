const std = @import("std");
const abi = @import("abi");

test "a domain shares the result enum of the domain that declares it" {
    try std.testing.expectError(error.ResultGeneral, abi.probeExtraCreate(0));
    const handle = try abi.probeExtraCreate(1);
    handle.destroy.?(handle.ref);
}
