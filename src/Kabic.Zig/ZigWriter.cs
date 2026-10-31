namespace Kabic.Zig;

using System.Text;

using Kabic;

/// <summary>
/// kabic's Zig writer: renders the ABI of one contract domain as a Zig module of extern
/// records, enum constants, callback types and opaque handles, laid out as the header lays
/// them out. A type another domain declares is reached through that domain's module, so a
/// type is one Zig type wherever it is used. A pointer says whether it reaches one value or
/// many from the header's tags, and stays nullable, as every C pointer is. Every member defaults
/// to zero. Rendering throws
/// <see cref="NotSupportedException"/> naming a declaration it cannot lay out.
/// </summary>
public sealed class ZigWriter
{
    /// <summary>What a slot's field adds to its name, since a method takes the name itself.</summary>
    public const string SlotSuffix = "_fn";

    readonly ApiModel model;
    readonly IReadOnlyDictionary<string, string> foreign;
    readonly SortedSet<string> imported = [];
    readonly HashSet<string> local;
    readonly Convention? convention;
    bool usesStd;
    bool usesResult;

    ZigWriter(ApiModel model, IReadOnlyDictionary<string, string> foreign, Convention? convention)
    {
        this.convention = convention;
        this.model = model;
        this.foreign = foreign;
        local = [
            .. model.Structs.Where(s => !s.External).Select(s => s.Name),
            .. model.Enums.Where(e => !e.External).Select(e => e.Name),
            .. model.TypeAliases.Keys,
            .. model.Opaques.Where(o => !foreign.ContainsKey(o)),
        ];
    }

    /// <summary>
    /// Renders the headers of one include root. <paramref name="foreign"/> maps each type
    /// another root declares to the name that root's module is imported as; a type found in
    /// neither this model nor the map is refused. A function is declared as external. Given
    /// <paramref name="convention"/>, a call that returns a result fails with <c>error.Failed</c>
    /// and answers with its one <c>out</c> parameter; such a function is also in <c>checked</c>.
    /// </summary>
    public static string Render(ApiModel model, IReadOnlyDictionary<string, string> foreign, Convention? convention = null) =>
        new ZigWriter(model, foreign, convention).Render();

    string Render()
    {
        var body = new StringBuilder();
        var head0 = "";

        foreach (var name in model.Opaques.Where(o => !foreign.ContainsKey(o)).Order(StringComparer.Ordinal))
            body.AppendLine($"pub const {name} = opaque {{}};");
        if (model.Opaques.Count > 0) body.AppendLine();

        foreach (var (alias, target) in model.TypeAliases.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (model.CallbackOf(alias) is not null) continue;
            body.AppendLine($"pub const {alias} = {Spell(target, null)};");
        }
        if (model.TypeAliases.Count > 0) body.AppendLine();

        foreach (var c in model.Constants) RenderConstant(body, c);
        if (model.Constants.Count > 0) body.AppendLine();

        foreach (var e in model.Enums.Where(e => !e.External)) RenderEnum(body, e);

        foreach (var c in model.Callbacks)
        {
            Doc(body, c.Doc, "");
            body.AppendLine($"pub const {c.Name} = {FnPointer(c.Lanes.Select(l => (l.Type, (ApiParam?)l)), c.Returns, null)};");
            body.AppendLine();
        }

        foreach (var s in model.Structs.Where(s => !s.External))
        {
            Doc(body, s.Doc, "");
            body.AppendLine($"pub const {s.Name} = {Record(s, "")};");
            body.AppendLine();
        }

        foreach (var data in model.Data)
        {
            body.AppendLine($"pub {(data.ReadOnly ? "const" : "var")} {data.Name}: {Spell(data.Type, null)} = {DataValue(data.Type, data.Value)};");
            body.AppendLine();
        }

        var refused = model.Variables.Select(v => v.Name).ToList();
        if (refused.Count > 0)
            throw new NotSupportedException(
                $"an extern variable has no projection: " + string.Join(", ", refused));

        foreach (var f in model.Functions)
        {
            Doc(body, f.Doc, "");
            var ps = f.Params.Select((p, i) => $"{Ident(p.Name ?? $"arg{i}")}: {Spell(p.Type, Lane.Of(p))}");
            body.AppendLine($"pub extern fn {f.Name}({string.Join(", ", ps)}) {Spell(f.CReturns ?? f.Returns, null)};");
            body.AppendLine();
        }

        var checkedFunctions = model.Functions.Where(f => convention?.ReturnsResult(f.Returns) == true).ToList();
        if (checkedFunctions.Count > 0)
        {
            body.AppendLine("/// The functions that return a result, failing with an error.");
            body.AppendLine("pub const checked = struct {");
            foreach (var f in checkedFunctions)
            {
                Doc(body, f.Doc, "    ");
                Wrapper(body, "    ", f.Name, null, [], $"root.{f.Name}", f.Params,
                    f.Params.Select((p, i) => Ident(p.Name is { Length: > 0 } n ? n : $"arg{i + 1}")).ToList(), f.Returns, f.CReturns ?? f.Returns, null);
            }
            body.AppendLine("};");
            body.AppendLine();
            head0 = "const root = @This();\n\n";
        }

        var head = new StringBuilder();
        head.AppendLine("//! <auto-generated/> The ABI of the contract headers, laid out as they lay it out. Do not edit; edit the C header instead.");
        head.AppendLine();
        if (usesStd) head.AppendLine("const std = @import(\"std\");");
        foreach (var module in imported) head.AppendLine($"const {module} = @import(\"{module}\");");
        if (usesStd || imported.Count > 0) head.AppendLine();
        head.Append(head0);
        if (usesResult) head.AppendLine(ResultErrors());
        return head.Append(body).ToString().TrimEnd('\n').ReplaceLineEndings("\n") + "\n";
    }

    string DataValue(string type, ApiValue v)
    {
        var bare = Bare(type);
        if (CTypes.FixedArray(type) is { } array)
            return ".{ " + string.Join(", ", v.Items.Select(i => DataValue(array.Element, i))) + " }";
        if (model.Structs.FirstOrDefault(s => s.Name == bare) is { } record)
            return ".{ " + string.Join(", ", record.Fields.Zip(v.Items, (f, i) => $".{Ident(f.Name)} = {DataValue(f.Type, i)}")) + " }";
        return v.Kind switch
        {
            ApiValueKind.Name => Value(v.Text),
            ApiValueKind.OffsetOf => v.Text.Split(',', StringSplitOptions.TrimEntries) is [var owner, var member]
                ? $"@offsetOf({Named(owner, byValue: true)}, \"{member}\")" : throw new NotSupportedException($"offsetof({v.Text})"),
            ApiValueKind.SizeOf => $"@sizeOf({Spell(v.Text, null)})",
            ApiValueKind.List => throw new NotSupportedException($"a list initializes {type}, which is no array or struct here"),
            _ => v.Text,
        };
    }

    /// <summary>A value an enum or a macro of this root or of another defines, reached as a type is.</summary>
    string Value(string name) =>
        model.Enums.Any(e => !e.External && e.Values.Any(v => v.Name == name)) || model.Constants.Any(c => c.Name == name)
            ? name : Named(name, byValue: true);

    /// <summary>
    /// One C enum as the integer type C gives it and a constant per value. A value is a
    /// constant rather than a case because the ABI carries the integer: a caller compares a
    /// value it may not know, which a Zig enum cannot hold.
    /// </summary>
    void RenderEnum(StringBuilder sb, ApiEnum e)
    {
        var signed = e.Values.Any(v => v.IsInt && long.Parse(v.RawValue) < 0);
        Doc(sb, e.Doc, "");
        sb.AppendLine($"pub const {e.Name} = {(signed ? "c_int" : "c_uint")};");
        foreach (var v in e.Values)
        {
            Doc(sb, v.Doc, "");
            sb.AppendLine($"pub const {v.Name}: {e.Name} = {v.RawValue};");
        }
        sb.AppendLine();
    }

    /// <summary>
    /// One macro constant. An integer the header types through a declared type keeps that type;
    /// one typed by C alone is a Zig integer literal, so its width is never one platform's.
    /// </summary>
    void RenderConstant(StringBuilder sb, ApiConstant c)
    {
        Doc(sb, c.Doc, "");
        if (c.Fields is { } fields)
            sb.AppendLine($"pub const {c.Name}: {Spell(c.Type, null)} = .{{ {string.Join(", ", fields.Select(f => $".{Ident(f.Name)} = {f.Value}"))} }};");
        else if (c.Type == "const char *" || Primitives.ContainsKey(Bare(c.Type)))
            sb.AppendLine($"pub const {c.Name} = {c.Value};");
        else
            sb.AppendLine($"pub const {c.Name}: {Spell(c.Type, null)} = {c.Value};");
    }

    /// <summary>
    /// A record as an extern struct or union, members in declaration order. A slot reads as a
    /// nullable function pointer whose first parameter is its receiver; a slot that receives the
    /// record itself is the field <c>slot_fn</c> and the method <c>slot</c>.
    /// </summary>
    string Record(ApiStruct s, string indent)
    {
        var sb = new StringBuilder();
        sb.AppendLine(s.IsUnion ? "extern union {" : "extern struct {");
        var members = s.Order ?? [.. s.Fields.Select(f => f.Name), .. s.Slots.Select(sl => sl.Name)];
        foreach (var name in members)
        {
            try { Member(sb, s, name, indent + "    "); }
            catch (NotSupportedException e) { throw new NotSupportedException($"{s.Name}.{name}: {e.Message}"); }
        }
        foreach (var slot in s.Slots.Where(sl => IsMethod(s, sl))) Method(sb, s, slot, indent + "    ");
        if (s.Has("owner") && s.Fields is [var owned] && s.Slots is [var destroy])
        {
            sb.AppendLine();
            sb.AppendLine($"{indent}    pub fn deinit(self: {s.Name}) void {{");
            sb.AppendLine($"{indent}        self.{Ident(destroy.Name)}.?(self.{Ident(owned.Name)});");
            sb.AppendLine($"{indent}    }}");
        }
        sb.Append(indent).Append('}');
        return sb.ToString();
    }

    static bool IsMethod(ApiStruct s, ApiSlot slot) =>
        s.IsVtable && !s.Has("bag") && Bare((slot.Receiver ?? s.Name + " *").Replace("*", " ")) == s.Name;

    void Method(StringBuilder sb, ApiStruct s, ApiSlot slot, string inner)
    {
        sb.AppendLine();
        Doc(sb, slot.Doc, inner);
        var names = slot.Params.Select((p, i) => ParamName(s, p, i)).ToList();
        Wrapper(sb, inner, Ident(slot.Name), $"self: *{s.Name}", ["self"], $"self.{slot.Name}{SlotSuffix}.?",
            slot.Params, names, slot.Returns, slot.CReturns ?? slot.Returns,
            slot.ReturnTagValue("array_of") is not null ? Lane.Many : null);
    }

    /// <summary>
    /// A function over <paramref name="target"/>. A result becomes an error, and the last
    /// parameter, when it alone is <c>out</c>, becomes the value returned.
    /// </summary>
    void Wrapper(StringBuilder sb, string inner, string name, string? receiver, string[] first, string target,
        IReadOnlyList<ApiParam> ps, IReadOnlyList<string> names, string returns, string cReturns, Lane? lane)
    {
        var result = convention?.ReturnsResult(returns) == true;
        var outs = ps.Where(p => p.Has("out")).ToList();
        var written = result && outs.Count == 1 && outs[0] == ps[^1] ? ps[^1] : null;
        var counts = ps.Where(p => SliceCount(p, ps) is not null).ToDictionary(p => SliceCount(p, ps)!, p => p);
        var taken = ps.Select((p, i) => (p, n: names[i])).Where(x => x.p != written && !counts.ContainsKey(x.p))
            .Select(x => $"{x.n}: {Taken(x.p, ps)}");
        var signature = string.Join(", ", (receiver is null ? taken : taken.Prepend(receiver)));
        var args = string.Join(", ", first.Concat(ps.Select((p, i) =>
            p == written ? "&written"
            : counts.TryGetValue(p, out var slice) ? $"@intCast({names[ps.ToList().IndexOf(slice)]}.len)"
            : SliceCount(p, ps) is not null ? $"{names[i]}.ptr"
            : IsString(p) ? (p.Has("optional") ? $"if ({names[i]}) |text| text.ptr else null" : $"{names[i]}.ptr")
            : names[i])));
        if (!result)
        {
            sb.AppendLine($"{inner}pub fn {name}({signature}) {Spell(cReturns, lane)} {{");
            sb.AppendLine($"{inner}    return {target}({args});");
            sb.AppendLine($"{inner}}}");
            return;
        }
        usesResult = true;
        var value = written is null ? "void" : Spell(CTypes.Deref(written.Type), null);
        sb.AppendLine($"{inner}pub fn {name}({signature}) Error!{value} {{");
        if (written is not null) sb.AppendLine($"{inner}    var written: {value} = undefined;");
        sb.AppendLine(ResultEnum() is null
            ? $"{inner}    if ({target}({args}) != {OkValue()}) return error.Failed;"
            : $"{inner}    try checkResult({target}({args}));");
        if (written is not null) sb.AppendLine($"{inner}    return written;");
        sb.AppendLine($"{inner}}}");
    }

    /// <summary>The count parameter of a pointer that takes a slice, or null.</summary>
    static ApiParam? SliceCount(ApiParam p, IReadOnlyList<ApiParam> ps) =>
        !p.Has("out") && p.TagValue("array_of") is { } count && !p.Has("optional") && CType.Of(p.Type) is { Pointers: 1, Name: not "void" }
            ? ps.FirstOrDefault(c => c.Name == count && !CTypes.IsPointer(c.Type)) : null;

    static bool IsString(ApiParam p) =>
        p.Has("utf8") && !p.Has("out") && !p.Has("array_of") && CType.Of(p.Type) is { Name: "char", Pointers: 1 };

    string Taken(ApiParam p, IReadOnlyList<ApiParam> ps)
    {
        if (SliceCount(p, ps) is not null)
            return $"[]{(CType.Of(p.Type).Const ? "const " : "")}{Spell(CTypes.Unqualified(CTypes.Deref(p.Type)), null)}";
        if (IsString(p)) return p.Has("optional") ? "?[:0]const u8" : "[:0]const u8";
        return Spell(p.Type, Lane.Of(p));
    }

    /// <summary>The enum of the result code when this root declares it.</summary>
    ApiEnum? ResultEnum() => model.Enums.FirstOrDefault(e => !e.External && e.Name == convention?.ResultType);

    /// <summary>One error per failing value of the result enum, and the function that raises it.</summary>
    string ResultErrors()
    {
        if (ResultEnum() is not { } e)
            return "/// A call returned a result other than success.\npub const Error = error{Failed};\n";
        var ok = OkValue();
        var failing = e.Values.Select(v => v.Name).Where(v => v != ok).Distinct().ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"/// The results of {e.Name} other than success; Failed is a value the header does not declare.");
        sb.AppendLine($"pub const Error = error{{ {string.Join(", ", failing.Append("Failed"))} }};");
        sb.AppendLine();
        sb.AppendLine($"fn checkResult(code: {e.Name}) Error!void {{");
        sb.AppendLine($"    if (code == {ok}) return;");
        foreach (var v in failing) sb.AppendLine($"    if (code == {v}) return error.{v};");
        sb.AppendLine("    return error.Failed;");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>The value of a call that succeeded: the number the convention names, or the enumerator ending in it.</summary>
    string OkValue()
    {
        var ok = convention!.ResultOk;
        if (long.TryParse(ok, out _)) return ok;
        var name = model.Enums.SelectMany(e => e.Values).Select(v => v.Name)
            .FirstOrDefault(v => v.EndsWith("_" + ok, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotSupportedException($"no enumerator is the result {ok}");
        return Value(name);
    }

    /// <summary>A parameter's name, which Zig refuses when it shadows a method of the record.</summary>
    static string ParamName(ApiStruct s, ApiParam p, int index)
    {
        var name = p.Name is { Length: > 0 } named ? named : $"arg{index + 1}";
        return s.Slots.Any(sl => sl.Name == name) || name == "self" ? $"{name}_" : Ident(name);
    }

    void Member(StringBuilder sb, ApiStruct s, string name, string inner)
    {
        if (s.Fields.FirstOrDefault(f => f.Name == name) is { } f)
        {
            Doc(sb, f.Doc, inner);
            var type = f.Inline is { } record ? Record(record, inner) : Spell(f.Type, Lane.Of(f));
            sb.AppendLine(s.IsUnion ? $"{inner}{Ident(f.Name)}: {type}," : $"{inner}{Ident(f.Name)}: {type} = {Zero(f, type)},");
            return;
        }
        var slot = s.Slots.First(sl => sl.Name == name);
        Doc(sb, slot.Doc, inner);
        var lanes = slot.Params.Select(p => (p.Type, (ApiParam?)p))
            .Prepend((slot.Receiver ?? $"{s.Name} *", null));
        var field = IsMethod(s, slot) ? slot.Name + SlotSuffix : Ident(slot.Name);
        sb.AppendLine($"{inner}{field}: {FnPointer(lanes, slot.CReturns ?? slot.Returns, slot.ReturnTagValue("array_of") is not null ? Lane.Many : null)} = null,");
    }

    /// <summary>
    /// The default of a struct member, since a union member takes none: zero, as C gives a member an initializer leaves out, so a
    /// params struct written with only the members it sets selects every other default.
    /// </summary>
    string Zero(ApiField field, string type)
    {
        if (field.Inline is not { } record) { usesStd = true; return $"std.mem.zeroes({type})"; }
        if (!record.IsUnion) return ".{}";
        var first = record.Fields[0];
        return $".{{ .{Ident(first.Name)} = {Zero(first, first.Inline is not null ? "" : Spell(first.Type, Lane.Of(first)))} }}";
    }

    string FnPointer(IEnumerable<(string Type, ApiParam? Param)> lanes, string returns, Lane? returned) =>
        $"?*const fn ({string.Join(", ", lanes.Select(l => Spell(l.Type, Lane.Of(l.Param))))}) callconv(.c) "
        + Spell(returns, returned);

    /// <summary>
    /// What the tags of a declaration say about the pointer it holds: whether it reaches one
    /// value or the first of many.
    /// </summary>
    sealed record Lane(bool IsMany)
    {
        public static readonly Lane Many = new(true);

        public static Lane? Of(ApiParam? p) => p is not null && p.Has("array_of") ? Many : null;

        public static Lane? Of(ApiField f) => f.Has("array_of") ? Many : null;
    }

    /// <summary>
    /// The Zig spelling of a C type. A fixed extent moves to the front, where Zig writes it.
    /// Every pointer level is nullable; the outermost reaches many values when the tags say
    /// so, and a pointer to <c>const char</c> is a terminated string unless they do.
    /// </summary>
    string Spell(string cType, Lane? lane)
    {
        if (CTypes.FixedArray(cType) is { } arr)
            return $"[{arr.Extent}]{Spell(arr.Element, null)}";
        if (model.CallbackOf(cType) is not null) return cType.Trim();
        if (CTypes.FunctionPointer(cType) is { Returns: { } returns } fn)
            return FnPointer(fn.ParamTypes.Select(t => (t, (ApiParam?)null)), returns, null);

        var levels = cType.Split('*');
        var bare = Bare(levels[0]);
        var depth = levels.Length - 1;
        if (depth == 0) return Named(bare, byValue: true);

        var spelled = (IsConst(levels[0]) ? "const " : "") + (bare == "void" ? "anyopaque" : Named(bare, byValue: false));
        for (var level = 1; level <= depth; level++)
        {
            var many = level == depth && lane is { IsMany: true };
            var marker = level == 1 && bare == "char" && IsConst(levels[0]) && !many ? "[*:0]"
                : level == 1 && bare == "void" ? "*"
                : many ? "[*]" : "*";
            spelled = $"?{marker}{spelled}";
            if (level < depth && IsConst(levels[level])) spelled = "const " + spelled;
        }
        return spelled;
    }

    static bool IsConst(string declarator) =>
        declarator.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("const");

    static string Bare(string cType) =>
        cType.Replace("const", " ").Replace("volatile", " ").Replace("struct ", " ").Replace("union ", " ")
            .Replace("enum ", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries) is var words
            ? string.Join(' ', words) : cType;

    string Named(string bare, bool byValue)
    {
        if (Primitives.TryGetValue(bare, out var prim)) return prim;
        if (local.Contains(bare)) return bare;
        if (foreign.TryGetValue(bare, out var module))
        {
            imported.Add(module);
            return $"{module}.{bare}";
        }
        throw new NotSupportedException(
            byValue
                ? $"{bare} is held by value and no domain describes it"
                : $"{bare} is pointed at and no domain declares it");
    }

    static readonly Dictionary<string, string> Primitives = new()
    {
        ["_Bool"] = "bool",
        ["bool"] = "bool",
        ["char"] = "u8",
        ["signed char"] = "i8",
        ["unsigned char"] = "u8",
        ["uint8_t"] = "u8",
        ["int8_t"] = "i8",
        ["uint16_t"] = "u16",
        ["int16_t"] = "i16",
        ["uint32_t"] = "u32",
        ["int32_t"] = "i32",
        ["uint64_t"] = "u64",
        ["int64_t"] = "i64",
        ["float"] = "f32",
        ["double"] = "f64",
        ["size_t"] = "usize",
        ["uintptr_t"] = "usize",
        ["intptr_t"] = "isize",
        ["void"] = "void",
        ["int"] = "c_int",
        ["unsigned"] = "c_uint",
        ["unsigned int"] = "c_uint",
        ["long"] = "c_long",
        ["unsigned long"] = "c_ulong",
        ["long long"] = "c_longlong",
        ["unsigned long long"] = "c_ulonglong",
    };

    static readonly HashSet<string> Reserved = [
        "addrspace", "align", "allowzero", "and", "anyframe", "anytype", "asm", "break", "callconv",
        "catch", "comptime", "const", "continue", "defer", "else", "enum", "errdefer", "error",
        "export", "extern", "fn", "for", "if", "inline", "linksection", "noalias", "noinline",
        "nosuspend", "opaque", "or", "orelse", "packed", "pub", "resume", "return", "struct",
        "suspend", "switch", "test", "threadlocal", "try", "type", "union", "unreachable", "var",
        "volatile", "while", "bool", "void", "true", "false", "null", "undefined",
    ];

    static string Ident(string name) =>
        Reserved.Contains(name) || IsPrimitiveTypeName(name) ? $"@\"{name}\"" : name;

    static bool IsPrimitiveTypeName(string name) =>
        name.Length > 1 && (name[0] is 'u' or 'i' or 'f') && name[1..].All(char.IsAsciiDigit)
        || name is "isize" or "usize" or "anyopaque" or "noreturn" or "comptime_int" or "comptime_float"
        || name.StartsWith("c_", StringComparison.Ordinal) && Primitives.ContainsValue(name);

    static void Doc(StringBuilder sb, string? doc, string indent)
    {
        if (doc is null) return;
        var words = doc.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder();
        foreach (var w in words)
        {
            if (line.Length > 0 && line.Length + w.Length > 92) { sb.AppendLine($"{indent}/// {line}"); line.Clear(); }
            if (line.Length > 0) line.Append(' ');
            line.Append(w);
        }
        if (line.Length > 0) sb.AppendLine($"{indent}/// {line}");
    }
}
