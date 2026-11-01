namespace Kabic.Zig;

using System.Text;

using Kabic;

/// <summary>
/// The Zig package of the headers of one domain: the module of their ABI under the name <c>abi</c>, the packages it imports as
/// dependencies, and a test that lays every record, alias, enum value and constant beside the
/// translation of the headers and fails where the two differ.
/// </summary>
/// <param name="Key">The name the package's module is imported under by the packages that
/// import it.</param>
/// <param name="Headers">The headers, relative to their include root.</param>
/// <param name="Imports">The packages whose types these headers compose.</param>
/// <param name="IncludeDirs">The include directories the headers are translated with, relative
/// to the package.</param>
/// <param name="Package">The package name its dependents declare.</param>
public sealed record ZigPackage(string Key, string Package, IReadOnlyList<string> Headers, IReadOnlyList<ZigImport> Imports,
    IReadOnlyList<string> IncludeDirs)
{
    /// <summary>The package name of the package of the contract domain <paramref name="domain"/>.</summary>
    public static string DomainPackage(string domain) => $"abi_{domain}";

    /// <summary>The <c>build.zig</c> of the package.</summary>
    public string BuildZig()
    {
        var sb = new StringBuilder();
        sb.AppendLine("const std = @import(\"std\");");
        sb.AppendLine();
        sb.AppendLine("pub fn build(b: *std.Build) void {");
        sb.AppendLine("    const target = b.standardTargetOptions(.{});");
        sb.AppendLine("    const optimize = b.standardOptimizeOption(.{});");
        sb.AppendLine();
        sb.AppendLine($"    const abi = b.addModule(\"abi\", .{{ .root_source_file = b.path(\"{Key}.zig\") }});");
        foreach (var i in Imports)
            sb.AppendLine($"    abi.addImport(\"{i.Key}\", b.dependency(\"{i.Package}\", .{{}}).module(\"abi\"));");
        sb.AppendLine();
        sb.AppendLine("    const headers = b.addWriteFiles().add(\"layout.h\",");
        foreach (var h in Headers)
            sb.AppendLine($"        \\\\#include <{h}>");
        sb.AppendLine("    );");
        sb.AppendLine("    const translated = b.addTranslateC(.{ .root_source_file = headers, .target = target, .optimize = optimize });");
        foreach (var dir in IncludeDirs)
            sb.AppendLine($"    translated.addIncludePath(b.path(\"{dir}\"));");
        sb.AppendLine("    const tests = b.addTest(.{ .root_module = b.createModule(.{");
        sb.AppendLine("        .root_source_file = b.path(\"layout_test.zig\"),");
        sb.AppendLine("        .link_libc = true,");
        sb.AppendLine("        .target = target,");
        sb.AppendLine("        .optimize = optimize,");
        sb.AppendLine("        .imports = &.{");
        sb.AppendLine("            .{ .name = \"abi\", .module = abi },");
        sb.AppendLine("            .{ .name = \"c\", .module = translated.createModule() },");
        sb.AppendLine("        },");
        sb.AppendLine("    }) });");
        sb.AppendLine("    tests.root_module.addCSourceFile(.{ .file = b.path(\"layout_values.c\") });");
        foreach (var dir in IncludeDirs)
            sb.AppendLine($"    tests.root_module.addIncludePath(b.path(\"{dir}\"));");
        sb.AppendLine($"    b.step(\"test\", \"Check the {Key} ABI against its headers\").dependOn(&b.addRunArtifact(tests).step);");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// The <c>build.zig.zon</c> of the package. Its fingerprint is derived from the name, so a
    /// regeneration writes the same one.
    /// </summary>
    public string BuildZon() => Zon(Package, Imports.Select(i => (i.Package, i.Path)));

    /// <summary>
    /// The layout test of the package: one check per record and alias, comparing size,
    /// alignment and the offset of every member, inline records included, one per enum
    /// value and one per macro constant.
    /// </summary>
    public static string LayoutTest(ApiModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("const std = @import(\"std\");");
        sb.AppendLine("const abi = @import(\"abi\");");
        sb.AppendLine("const c = @import(\"c\");");
        sb.AppendLine();
        sb.AppendLine("fn expectSameLayout(comptime A: type, comptime C: type) !void {");
        sb.AppendLine("    @setEvalBranchQuota(100_000_000);");
        sb.AppendLine("    if (@typeInfo(C) == .@\"opaque\") return;");
        sb.AppendLine("    try std.testing.expectEqual(@sizeOf(C), @sizeOf(A));");
        sb.AppendLine("    try std.testing.expectEqual(@alignOf(C), @alignOf(A));");
        sb.AppendLine("    switch (@typeInfo(A)) {");
        sb.AppendLine("        .@\"struct\" => |s| inline for (s.fields) |f| {");
        sb.AppendLine("            try std.testing.expectEqual(@offsetOf(C, f.name), @offsetOf(A, f.name));");
        sb.AppendLine("            try expectSameLayout(f.type, @FieldType(C, f.name));");
        sb.AppendLine("        },");
        sb.AppendLine("        .@\"union\" => |u| inline for (u.fields) |f| try expectSameLayout(f.type, @FieldType(C, f.name)),");
        sb.AppendLine("        else => {},");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("fn translated(comptime name: []const u8) type {");
        sb.AppendLine("    return if (@hasDecl(c, name)) @field(c, name) else @field(c, \"struct_\" ++ name);");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("test \"every record is laid out as the header lays it out\" {");
        sb.AppendLine("    @setEvalBranchQuota(100_000_000);");
        foreach (var s in model.Structs.Where(s => !s.External))
            sb.AppendLine($"    try expectSameLayout(abi.c.{s.Name}, translated(\"{s.Name}\"));");
        foreach (var (alias, _) in model.TypeAliases.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            sb.AppendLine($"    if (@hasDecl(c, \"{alias}\")) try expectSameLayout(abi.c.{alias}, c.{alias});");
        sb.AppendLine("}");
        var values = model.Enums.Where(e => !e.External).SelectMany(e => e.Values).ToList();
        if (values.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("test \"every enum value is the header's value\" {");
            sb.AppendLine("    @setEvalBranchQuota(100_000_000);");
            foreach (var v in values)
                sb.AppendLine($"    try std.testing.expectEqual(@as(i64, c.{v.Name}), @as(i64, abi.c.{v.Name}));");
            sb.AppendLine("}");
        }
        if (model.Constants.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("test \"every constant is the header's value\" {");
            sb.AppendLine("    @setEvalBranchQuota(100_000_000);");
            foreach (var k in model.Constants)
                sb.AppendLine(k.Fields is not null
                    ? $"    try std.testing.expectEqual(std.mem.toBytes(c.{k.Name}), std.mem.toBytes(abi.c.{k.Name}));"
                    : k.Type == "const char *"
                        ? $"    try std.testing.expectEqualStrings(c.{k.Name}, abi.c.{k.Name});"
                        : $"    try std.testing.expectEqual(@as(i128, c.{k.Name}), @as(i128, abi.c.{k.Name}));");
            sb.AppendLine("}");
        }
        var measures = Measures(model).ToList();
        sb.AppendLine();
        sb.AppendLine($"extern const layout_values: [{measures.Count + 1}]c_ulonglong;");
        sb.AppendLine();
        sb.AppendLine("test \"every size and offset is the one C computes\" {");
        sb.AppendLine("    @setEvalBranchQuota(100_000_000);");
        sb.AppendLine("    const expected = [_]usize{");
        foreach (var (type, member) in measures)
            sb.AppendLine(member is null ? $"        @sizeOf(abi.c.{type})," : $"        @offsetOf(abi.c.{type}, \"{member}\"),");
        sb.AppendLine("    };");
        sb.AppendLine("    for (expected, 0..) |value, at| try std.testing.expectEqual(layout_values[at], value);");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// What C itself says of every record, as an array: its size, then the offset of each member
    /// that has one. A record the translation leaves opaque is checked by this alone.
    /// </summary>
    public string LayoutValues(ApiModel model)
    {
        var sb = new StringBuilder("#include <stddef.h>\n");
        foreach (var h in Headers) sb.AppendLine($"#include <{h}>");
        sb.AppendLine("const unsigned long long layout_values[] = {");
        foreach (var (type, member) in Measures(model))
            sb.AppendLine(member is null ? $"    sizeof({type})," : $"    offsetof({type}, {member}),");
        sb.AppendLine("    0,");
        sb.AppendLine("};");
        return sb.ToString();
    }

    static IEnumerable<(string Type, string? Member)> Measures(ApiModel model)
    {
        foreach (var s in model.Structs.Where(s => !s.External))
        {
            yield return (s.Name, null);
            if (s.IsUnion) continue;
            foreach (var f in s.Fields.Where(f => f.Bits is null && !f.Name.StartsWith("unnamed_", StringComparison.Ordinal)))
                yield return (s.Name, f.Name);
            foreach (var slot in s.Slots) yield return (s.Name, slot.Name);
        }
    }

    /// <summary>The package name of the package that gathers every package.</summary>
    public const string AllName = "abi";

    /// <summary>
    /// The <c>build.zig</c> of the package that gathers every package: its <c>test</c> step runs
    /// every package's layout test.
    /// </summary>
    public static string AllBuildZig(IReadOnlyList<ZigImport> packages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("const std = @import(\"std\");");
        sb.AppendLine();
        sb.AppendLine("pub fn build(b: *std.Build) void {");
        sb.AppendLine("    const target = b.standardTargetOptions(.{});");
        sb.AppendLine("    const optimize = b.standardOptimizeOption(.{});");
        sb.AppendLine("    const tests = b.step(\"test\", \"Check the ABI of every domain against the headers\");");
        sb.AppendLine("    inline for (.{");
        foreach (var p in packages) sb.AppendLine($"        \"{p.Package}\",");
        sb.AppendLine("    }) |name| {");
        sb.AppendLine("        const domain = b.dependency(name, .{ .target = target, .optimize = optimize });");
        sb.AppendLine("        tests.dependOn(&domain.builder.top_level_steps.get(\"test\").?.step);");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>The <c>build.zig.zon</c> of the package that gathers every package.</summary>
    public static string AllBuildZon(IReadOnlyList<ZigImport> packages) =>
        Zon(AllName, packages.Select(p => (p.Package, p.Path)));

    static string Zon(string name, IEnumerable<(string Name, string Path)> dependencies)
    {
        var sb = new StringBuilder();
        sb.AppendLine(".{");
        sb.AppendLine($"    .name = .{name},");
        sb.AppendLine("    .version = \"0.1.0\",");
        sb.AppendLine($"    .fingerprint = 0x{Fingerprint(name):x16},");
        sb.AppendLine("    .minimum_zig_version = \"0.16.0\",");
        sb.AppendLine("    .dependencies = .{");
        foreach (var (dep, path) in dependencies)
            sb.AppendLine($"        .{dep} = .{{ .path = \"{path}\" }},");
        sb.AppendLine("    },");
        sb.AppendLine("    .paths = .{\".\"},");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// A package fingerprint as Zig checks it: the CRC-32 of the name in the upper half and an
    /// identifier in the lower half that is neither zero nor all ones.
    /// </summary>
    static ulong Fingerprint(string name)
    {
        var checksum = Crc32(Encoding.UTF8.GetBytes(name));
        var id = Crc32(Encoding.UTF8.GetBytes("kabic/" + name));
        if (id is 0 or uint.MaxValue) id = 1;
        return ((ulong)checksum << 32) | id;
    }

    static uint Crc32(byte[] bytes)
    {
        var crc = uint.MaxValue;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}

/// <summary>
/// A package another one depends on: the name its module is imported under, its package name,
/// and its directory, relative to the dependent.
/// </summary>
public sealed record ZigImport(string Key, string Package, string Path);
