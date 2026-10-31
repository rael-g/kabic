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
    readonly ApiModel model;
    readonly IReadOnlyDictionary<string, string> foreign;
    readonly SortedSet<string> imported = [];
    readonly HashSet<string> local;
    readonly Convention? convention;
    bool usesStd;
    bool usesResult;
    bool top;
    string abiFile = "";
    string abiText = "";
    readonly HashSet<string> topNames = ["c", "Error", "root"];

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

    /// <summary>
    /// Renders the layer a caller uses and the ABI under it, which that layer imports as
    /// <paramref name="abiFile"/> and exposes as <c>c</c>.
    /// </summary>
    public static (string Api, string Abi) RenderFiles(ApiModel model, IReadOnlyDictionary<string, string> foreign, Convention convention, string abiFile)
    {
        var writer = new ZigWriter(model, foreign, convention) { abiFile = abiFile };
        return (writer.Render(), writer.abiText);
    }

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

        var idiom = new StringBuilder();
        if (convention is not null) Idiomatic(idiom);
        var checkedFunctions = new List<ApiFunction>();
        if (checkedFunctions.Count > 0)
        {
            body.AppendLine("/// The functions that return a result, failing with an error.");
            body.AppendLine("pub const checked = struct {");
            foreach (var f in checkedFunctions)
            {
                Doc(body, f.Doc, "    ");
                Wrapper(body, "    ", Ident(FunctionName(f.Name)), null, [], $"root.{f.Name}", f.Params,
                    f.Params.Select((p, i) => Ident(p.Name is { Length: > 0 } n ? n : $"arg{i + 1}")).ToList(), f.Returns, f.CReturns ?? f.Returns, null);
            }
            body.AppendLine("};");
            body.AppendLine();
        }

        var head = new StringBuilder();
        head.AppendLine("//! <auto-generated/> The ABI of the contract headers, laid out as they lay it out. Do not edit; edit the C header instead.");
        head.AppendLine();
        if (usesStd) head.AppendLine("const std = @import(\"std\");");
        foreach (var module in imported) head.AppendLine($"const {module} = @import(\"{module}\");");
        if (usesStd || imported.Count > 0) head.AppendLine();
        head.Append(head0);
        if (convention is not null)
        {
            var rawHead = head.ToString();
            head.AppendLine($"/// The ABI as the headers declare it.\npub const c = @import(\"{abiFile}\");\n");
            top = true;
            if (usesResult) head.AppendLine(ResultErrors());
            top = false;
            head.Append(idiom);
            abiText = rawHead + body.ToString().TrimEnd('\n').ReplaceLineEndings("\n") + "\n";
            return head.ToString().TrimEnd('\n').ReplaceLineEndings("\n") + "\n";
        }
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
            ? (top ? $"c.{name}" : name) : Named(name, byValue: true);

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
    /// nullable function pointer whose first parameter is its receiver.
    /// </summary>
    string Record(ApiStruct s, string indent)
    {
        var sb = new StringBuilder();
        sb.AppendLine(s.IsUnion ? "extern union {" : "extern struct {");
        var members = s.Order ?? [.. s.Fields.Select(f => f.Name), .. s.Slots.Select(sl => sl.Name)];
        var run = new List<ApiField>();
        foreach (var name in members)
        {
            if (s.Fields.FirstOrDefault(f => f.Name == name) is { Bits: not null } bits)
            {
                if (run.Count > 0 && (BitsOf(run[0].Type) != BitsOf(bits.Type) || run.Sum(f => f.Bits!.Value) + bits.Bits!.Value > BitsOf(bits.Type)))
                    BitRun(sb, run, indent + "    ");
                run.Add(bits);
                continue;
            }
            BitRun(sb, run, indent + "    ");
            try { Member(sb, s, name, indent + "    "); }
            catch (NotSupportedException e) { throw new NotSupportedException($"{s.Name}.{name}: {e.Message}"); }
        }
        BitRun(sb, run, indent + "    ");
        sb.Append(indent).Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// The bit-fields that share one storage unit, as a packed struct of that unit, since an
    /// extern struct holds no field narrower than a byte. The field takes the first one's name.
    /// </summary>
    void BitRun(StringBuilder sb, List<ApiField> run, string inner)
    {
        if (run.Count == 0) return;
        var width = BitsOf(run[0].Type);
        sb.AppendLine($"{inner}{run[0].Name}_bits: packed struct(u{width}) {{");
        foreach (var f in run) sb.AppendLine($"{inner}    {Ident(f.Name)}: u{f.Bits} = 0,");
        var used = run.Sum(f => f.Bits!.Value);
        if (used < width) sb.AppendLine($"{inner}    _padding: u{width - used} = 0,");
        sb.AppendLine($"{inner}}} = .{{}},");
        run.Clear();
    }

    int BitsOf(string type)
    {
        var spelled = Primitives.GetValueOrDefault(Bare(model.ResolveAlias(type)), "c_uint");
        return spelled is "c_int" or "c_uint" ? 32 : int.Parse(spelled[1..]);
    }

    static bool IsMethod(ApiStruct s, ApiSlot slot) =>
        s.IsVtable && !s.Has("bag") && Bare((slot.Receiver ?? s.Name + " *").Replace("*", " ")) == s.Name;

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
            : WrappedType(p.Type) is not null ? $"{names[i]}.ptr"
            : LocalEnum(p.Type) is not null ? $"@intFromEnum({names[i]})"
            : names[i])));
        if (!result)
        {
            var wrapped = lane is null && CType.Of(cReturns) is { Const: false } ? WrappedType(cReturns) : null;
            var localEnum = LocalEnum(cReturns);
            var spelled = wrapped is not null ? $"?{wrapped}" : localEnum ?? Spell(cReturns, lane);
            sb.AppendLine($"{inner}pub fn {name}({signature}) {spelled} {{");
            sb.AppendLine(wrapped is not null
                ? $"{inner}    return if ({target}({args})) |ptr| {wrapped}{{ .ptr = ptr }} else null;"
                : localEnum is not null ? $"{inner}    return @enumFromInt({target}({args}));"
                : $"{inner}    return {target}({args});");
            sb.AppendLine($"{inner}}}");
            return;
        }
        usesResult = true;
        var raw = written is null ? "void" : Spell(CTypes.Deref(written.Type), null);
        var (value, answer) = Answer(written, raw);
        sb.AppendLine($"{inner}pub fn {name}({signature}) Error!{value} {{");
        if (written is not null) sb.AppendLine($"{inner}    var written: {raw} = undefined;");
        sb.AppendLine(ResultEnum() is null && ResultModule() is null
            ? $"{inner}    if ({target}({args}) != {OkValue()}) return error.Failed;"
            : $"{inner}    try checkResult({target}({args}));");
        if (written is not null) sb.AppendLine($"{inner}    return {answer};");
        sb.AppendLine($"{inner}}}");
    }

    (string Value, string Expression) Answer(ApiParam? written, string raw)
    {
        if (written is null) return (raw, "");
        if (model.Structs.FirstOrDefault(o => o.Has("owner") && o.Name == CType.Of(written.Type).Name) is { Fields: [var held] } owner
            && WrappedType(held.Type) is { } type)
            return (type, $".{{ .ptr = written.{Ident(held.Name)} orelse return error.Failed, .owner = written }}");
        var deref = CTypes.Deref(written.Type);
        return WrappedType(deref) is { } handle ? (handle, ".{ .ptr = written orelse return error.Failed }") : (raw, "written");
    }

    /// <summary>The type that wraps a pointer to a local vtable or opaque record, or null.</summary>
    string? WrappedType(string cType) =>
        top && Handled(cType) is { } name ? TypeName(name) : null;

    /// <summary>The wrapped declaration a parameter of <paramref name="cType"/> passes: a pointer to it, or a pointer typedef itself.</summary>
    string? Handled(string cType) => CType.Of(cType) switch
    {
        { Pointers: 1 } t when Wrappable(t.Name) => t.Name,
        { Pointers: 0 } t when IsPointerTypedef(t.Name) => t.Name,
        _ => null,
    };

    bool IsPointerTypedef(string name) =>
        model.TypeAliases.ContainsKey(name) && !foreign.ContainsKey(name) && model.HandleOf(name) is not null;

    bool Claim(string name, string declaration)
    {
        if (topNames.Add(name)) return true;
        Console.Error.WriteLine($"{declaration}: {name} is already declared, so it stays reachable only through c");
        return false;
    }

    bool Wrappable(string name) =>
        model.Opaques.Contains(name) && !foreign.ContainsKey(name) && !model.TypeAliases.Values.Any(v => CType.Of(v).Name == name)
        || model.Structs.Any(s => s.Name == name && !s.External && s.IsVtable && !s.Has("bag") && !s.Has("owner"));

    string? LocalEnum(string cType) =>
        top && model.Enums.FirstOrDefault(e => !e.External && e.Name == cType.Trim()) is { } e ? TypeName(e.Name) : null;

    string TypeName(string name) => Pascal(convention!.Spell(Languages.Zig, convention.StripPrefix(name)));

    static string[] CaseWords(ApiEnum e)
    {
        var names = e.Values.Select(v => v.Name.Split('_')).ToList();
        var common = 0;
        while (names.All(n => common < n.Length - 1 && n[common] == names[0][common])) common++;
        return names.Select(n => string.Join('_', n.Skip(common)).ToLowerInvariant()).ToArray();
    }

    /// <summary>Everything a caller uses: enums, types with methods, functions, and aliases of the rest.</summary>
    void Idiomatic(StringBuilder o)
    {
        top = true;
        usesResult = ResultEnum() is not null;
        if (ResultModule() is { } resultModule) { imported.Add(resultModule); usesResult = true; }
        var types = model.Structs.Where(s => !s.External && s.IsVtable && !s.Has("bag") && !s.Has("owner")).Select(s => s.Name)
            .Concat(model.Opaques.Where(n => Wrappable(n))).Concat(model.TypeAliases.Keys.Where(IsPointerTypedef)).Distinct().ToList();
        foreach (var name in types)
            if (!topNames.Add(TypeName(name))) throw new NotSupportedException($"{name} and another type are both {TypeName(name)}");
        var wrappedNames = types.ToHashSet();
        foreach (var e in model.Enums.Where(e => !e.External && e.Values.Any(v => v.IsInt)))
        {
            if (!Claim(TypeName(e.Name), e.Name)) continue;
            var cases = CaseWords(e);
            var seen = new HashSet<string>();
            var used = new HashSet<string>();
            o.AppendLine($"pub const {TypeName(e.Name)} = enum({Spell(e.Name, null)}) {{");
            foreach (var (v, i) in e.Values.Select((v, i) => (v, i)))
                if (v.IsInt && seen.Add(v.RawValue) && used.Add(cases[i])) o.AppendLine($"    {CaseIdent(cases[i])} = {v.RawValue},");
            o.AppendLine("    _,");
            o.AppendLine("};");
            o.AppendLine();
        }
        var owned = new Dictionary<string, List<ApiFunction>>();
        var free = new List<ApiFunction>();
        foreach (var f in model.Functions)
        {
            var home = f.Has("free") ? null : f.Params.Count > 0 && Handled(f.Params[0].Type) is { } first && types.Contains(first) ? first
                : f.Params.LastOrDefault(p => p.Has("out")) is { } last ? Creates(last, types) : null;
            if (home is null) free.Add(f);
            else { if (!owned.TryGetValue(home, out var list)) owned[home] = list = []; list.Add(f); }
        }
        foreach (var name in types)
        {
            var record = model.Structs.FirstOrDefault(s => s.Name == name);
            var owner = model.Structs.FirstOrDefault(s => s.Has("owner") && s.Fields is [var h] && CType.Of(h.Type).Name == name);
            var type = TypeName(name);
            var members = new HashSet<string>();
            o.AppendLine($"pub const {type} = struct {{");
            o.AppendLine(IsPointerTypedef(name) ? $"    ptr: {Named(name, true)}," : $"    ptr: *{Named(name, false)},");
            if (owner is not null) o.AppendLine($"    owner: ?{Named(owner.Name, true)} = null,");
            o.AppendLine();
            if (owner is { Fields: [var field], Slots: [var destroy] } && members.Add("deinit"))
            {
                o.AppendLine($"    pub fn deinit(self: {type}) void {{");
                o.AppendLine($"        if (self.owner) |owner| owner.{Ident(destroy.Name)}.?(owner.{Ident(field.Name)});");
                o.AppendLine("    }");
                o.AppendLine();
            }
            if (record is not null) Implement(o, record);
            foreach (var slot in (record?.Slots ?? []).Where(sl => record is not null && IsMethod(record, sl)))
            {
                var method = FunctionName(slot.Name);
                if (!members.Add(method)) continue;
                Doc(o, slot.Doc, "    ");
                Wrapper(o, "    ", Ident(method), $"self: {type}", ["self.ptr"], $"self.ptr.{Ident(slot.Name)}.?", slot.Params,
                    slot.Params.Select((p, i) => ParamName(record!, p, i)).ToList(), slot.Returns, slot.CReturns ?? slot.Returns,
                    slot.ReturnTagValue("array_of") is not null ? Lane.Many : null);
                o.AppendLine();
            }
            foreach (var f in owned.GetValueOrDefault(name) ?? [])
            {
                var method = FunctionName(Unprefixed(f.Name, name));
                if (!members.Add(method))
                {
                    Console.Error.WriteLine($"{f.Name}: {type}.{method} is already declared, so it stays reachable only through c");
                    continue;
                }
                var isMethod = f.Params.Count > 0 && Handled(f.Params[0].Type) == name;
                var ps = isMethod ? f.Params.Skip(1).ToList() : f.Params.ToList();
                Doc(o, f.Doc, "    ");
                Wrapper(o, "    ", Ident(method), isMethod ? $"self: {type}" : null, isMethod ? ["self.ptr"] : [], $"c.{f.Name}", ps,
                    ps.Select((p, i) => FreeParam(p, i, members)).ToList(), f.Returns, f.CReturns ?? f.Returns, null);
                o.AppendLine();
            }
            o.AppendLine("};");
            o.AppendLine();
        }
        foreach (var f in free)
        {
            var name = FunctionName(f.Name);
            if (!Claim(name, f.Name)) continue;
            Doc(o, f.Doc, "");
            Wrapper(o, "", Ident(name), null, [], $"c.{f.Name}", f.Params,
                f.Params.Select((p, i) => FreeParam(p, i, topNames)).ToList(), f.Returns, f.CReturns ?? f.Returns, null);
            o.AppendLine();
        }
        foreach (var s in model.Structs.Where(s => !s.External && !wrappedNames.Contains(s.Name)))
            if (Claim(TypeName(s.Name), s.Name)) o.AppendLine($"pub const {TypeName(s.Name)} = c.{s.Name};");
        o.AppendLine();
        top = false;
    }

    string? Creates(ApiParam written, List<string> types)
    {
        var name = CType.Of(written.Type).Name;
        if (types.Contains(name) && CType.Of(written.Type).Pointers == (IsPointerTypedef(name) ? 1 : 2)) return name;
        return model.Structs.FirstOrDefault(s => s.Has("owner") && s.Name == name) is { Fields: [var held] }
            && types.Contains(CType.Of(held.Type).Name) ? CType.Of(held.Type).Name : null;
    }

    string Unprefixed(string function, string type) =>
        convention!.StripPrefix(function) is var name && name.StartsWith(convention.StripPrefix(type) + "_", StringComparison.Ordinal)
            ? name[(convention.StripPrefix(type).Length + 1)..] : name;

    static string CaseIdent(string name) =>
        name.Length > 0 && !char.IsAsciiDigit(name[0]) && !Reserved.Contains(name) ? Ident(name) : $"@\"{name}\"";

    static string FreeParam(ApiParam p, int index, HashSet<string> taken)
    {
        var name = p.Name is { Length: > 0 } named ? named : $"arg{index + 1}";
        return taken.Contains(name) || name == "self" ? $"{name}_" : Ident(name);
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
        return WrappedType(p.Type) ?? LocalEnum(p.Type) ?? Spell(p.Type, Lane.Of(p));
    }

    /// <summary>A name in Zig's camelCase, without the prefix of the library; unchanged without a convention.</summary>
    string FunctionName(string name)
    {
        if (convention is null) return name;
        var pascal = Pascal(convention.Spell(Languages.Zig, convention.StripPrefix(name)));
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    static string Pascal(string name) =>
        string.Concat(name.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    /// <summary>The enum of the result code when this root declares it.</summary>
    ApiEnum? ResultEnum() => convention is null ? null : model.Enums.FirstOrDefault(e => !e.External && e.Name == convention?.ResultType);

    /// <summary>One error per failing value of the result enum, and the function that raises it.</summary>
    string ResultErrors()
    {
        if (ResultModule() is { } module)
            return $"pub const Error = {module}.Error;\nconst checkResult = {module}.checkResult;\nconst resultOf = {module}.resultOf;\n";
        if (ResultEnum() is not { } e)
            return "/// A call returned a result other than success.\npub const Error = error{Failed};\n";
        var ok = OkValue();
        var cases = CaseWords(e);
        var failing = e.Values.Select((v, i) => (Name: v.Name, Error: Pascal(cases[i]))).Where(v => $"c.{v.Name}" != ok).DistinctBy(v => v.Error).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"/// The results of {e.Name} other than success; Failed is a value the header does not declare.");
        sb.AppendLine($"pub const Error = error{{ {string.Join(", ", failing.Select(v => v.Error).Append("Failed").Distinct())} }};");
        sb.AppendLine();
        sb.AppendLine($"pub fn checkResult(code: c.{e.Name}) Error!void {{");
        sb.AppendLine($"    if (code == {ok}) return;");
        foreach (var v in failing) sb.AppendLine($"    if (code == c.{v.Name}) return error.{v.Error};");
        sb.AppendLine("    return error.Failed;");
        sb.AppendLine("}");
        if (failing.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"/// The code of an error, for a slot implemented in Zig; Failed takes the first failing value.");
            sb.AppendLine($"pub fn resultOf(failure: Error) c.{e.Name} {{");
            sb.AppendLine("    return switch (failure) {");
            foreach (var v in failing) sb.AppendLine($"        error.{v.Error} => c.{v.Name},");
            if (failing.All(v => v.Error != "Failed")) sb.AppendLine($"        error.Failed => c.{failing[0].Name},");
            sb.AppendLine("    };");
            sb.AppendLine("}");
        }
        return sb.ToString();
    }

    /// <summary>The module that declares the result enum, when another root does.</summary>
    string? ResultModule() =>
        convention is not null && foreign.TryGetValue(convention.ResultType, out var module) ? module : null;

    /// <summary>The body of <c>implement</c> for the vtable, or null when a result has no way back.</summary>
    void Implement(StringBuilder o, ApiStruct record)
    {
        var slots = record.Slots.Where(sl => IsMethod(record, sl)).ToList();
        if (slots.Count == 0) return;
        if (slots.Any(sl => convention!.ReturnsResult(sl.Returns)) && ResultEnum() is null && ResultModule() is null) return;
        o.AppendLine($"    /// The vtable whose slots call the methods of <paramref name=\"T\"/>, which embeds it as the field <c>api</c>.".Replace("<paramref name=\"T\"/>", "T").Replace("<c>api</c>", "api"));
        o.AppendLine($"    pub fn implement(comptime T: type) {Named(record.Name, true)} {{");
        o.AppendLine("        return .{");
        foreach (var slot in slots) ImplementSlot(o, record, slot);
        o.AppendLine("        };");
        o.AppendLine("    }");
        o.AppendLine();
    }

    void ImplementSlot(StringBuilder o, ApiStruct record, ApiSlot slot)
    {
        var ps = slot.Params;
        var result = convention!.ReturnsResult(slot.Returns);
        var lane = slot.ReturnTagValue("array_of") is not null ? Lane.Many : null;
        var cReturn = slot.CReturns ?? slot.Returns;
        var returned = Spell(cReturn, lane);
        var refuse = result ? "return resultOf(error.Failed)" : cReturn.Trim() == "void" ? "return" : $"return std.mem.zeroes({returned})";
        if (!result && cReturn.Trim() != "void") usesStd = true;
        var outs = ps.Where(p => p.Has("out")).ToList();
        var written = result && outs.Count == 1 && outs[0] == ps[^1] ? ps[^1] : null;
        var counts = ps.Where(p => SliceCount(p, ps) is not null).ToDictionary(p => SliceCount(p, ps)!, p => p);
        var name = (ApiParam p) => $"a{ps.ToList().IndexOf(p)}";
        var signature = string.Join(", ", ps.Select(p => $"{name(p)}: {Spell(p.Type, Lane.Of(p))}").Prepend($"self: {Spell(slot.Receiver ?? record.Name + " *", null)}"));
        var body = new List<string> { $"const state: *T = @alignCast(@fieldParentPtr(\"api\", self orelse {refuse}));" };
        var args = new List<string> { "state" };
        foreach (var p in ps.Where(p => p != written && !counts.ContainsKey(p)))
        {
            var n = name(p);
            if (SliceCount(p, ps) is { } count) args.Add($"if ({n}) |items| items[0..{name(count)}] else &.{{}}");
            else if (IsString(p)) { usesStd = true; args.Add(p.Has("optional") ? $"if ({n}) |text| std.mem.span(text) else null" : $"std.mem.span({n} orelse {refuse})"); }
            else if (WrappedType(p.Type) is { } wrapped) args.Add($"{wrapped}{{ .ptr = {n} orelse {refuse} }}");
            else if (LocalEnum(p.Type) is not null) args.Add($"@enumFromInt({n})");
            else args.Add(n);
        }
        var call = $"T.{Ident(FunctionName(slot.Name))}({string.Join(", ", args)})";
        if (result)
        {
            if (written is null) body.Add($"{call} catch |failure| return resultOf(failure);");
            else
            {
                var (_, _) = Answer(written, "");
                body.Add($"const value = {call} catch |failure| return resultOf(failure);");
                var deref = CTypes.Deref(written.Type);
                body.Add($"({name(written)} orelse {refuse}).* = value{(WrappedType(deref) is not null ? ".ptr" : "")};");
            }
            body.Add($"return {OkValue()};");
        }
        else if (LocalEnum(cReturn) is not null) body.Add($"return @intFromEnum({call});");
        else if (lane is null && CType.Of(cReturn) is { Const: false } && WrappedType(cReturn) is not null)
            body.Add($"return if ({call}) |handle| handle.ptr else null;");
        else body.Add($"return {call};");
        o.AppendLine($"            .{Ident(slot.Name)} = &struct {{");
        o.AppendLine($"                fn call({signature}) callconv(.c) {returned} {{");
        foreach (var line in body) o.AppendLine($"                    {line}");
        o.AppendLine("                }");
        o.AppendLine("            }.call,");
    }

    /// <summary>The value of a call that succeeded: the number the convention names, or the enumerator ending in it.</summary>
    string OkValue()
    {
        var ok = convention!.ResultOk;
        if (long.TryParse(ok, out _)) return ok;
        var name = model.Enums.SelectMany(e => e.Values).Select(v => v.Name)
            .FirstOrDefault(v => v.Replace("_", "").EndsWith(ok, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotSupportedException($"no enumerator is the result {ok}");
        return Value(name);
    }

    /// <summary>A parameter's name, which Zig refuses when it shadows a method of the record.</summary>
    string ParamName(ApiStruct s, ApiParam p, int index)
    {
        var name = p.Name is { Length: > 0 } named ? named : $"arg{index + 1}";
        return s.Slots.Any(sl => sl.Name == name || FunctionName(sl.Name) == name) || name == "self" ? $"{name}_" : Ident(name);
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
        sb.AppendLine($"{inner}{Ident(slot.Name)}: {FnPointer(lanes, slot.CReturns ?? slot.Returns, slot.ReturnTagValue("array_of") is not null ? Lane.Many : null)} = null,");
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
        if (model.CallbackOf(cType) is not null) return (top ? "c." : "") + cType.Trim();
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
        if (local.Contains(bare)) return top ? $"c.{bare}" : bare;
        if (foreign.TryGetValue(bare, out var module))
        {
            imported.Add(module);
            return convention is null ? $"{module}.{bare}" : $"{module}.c.{bare}";
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
