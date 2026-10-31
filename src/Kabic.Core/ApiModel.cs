
namespace Kabic;

/// <summary>
/// A parameter of a function, a slot or a callback lane, with the tags its declaration carries.
/// </summary>
public record ApiParam(string? Name, string Type, IReadOnlyList<string> Tags, string? Doc)
{
    /// <summary>The callback describing a function pointer written inline in the parameter.</summary>
    public string? Callback { get; init; }

    /// <summary>
    /// Whether the parameter carries <paramref name="tag"/>, with or without a value.
    /// </summary>
    public bool Has(string tag) => Tags.Any(t => t == tag || t.StartsWith(tag + ":"));

    /// <summary>
    /// The text after the colon of the first tag named <paramref name="tag"/>, or null.
    /// </summary>
    public string? TagValue(string tag) => Tags.FirstOrDefault(t => t.StartsWith(tag + ":"))?[(tag.Length + 1)..];
}

/// <summary>
/// A function-pointer slot of a vtable, with its return, parameters and tags as declared.
/// </summary>
public record ApiSlot(string Name, string Returns, IReadOnlyList<string> Tags, string? Doc, string? ReturnDoc, IReadOnlyList<ApiParam> Params)
{
    /// <summary>The return type C declares, where <c>Returns</c> holds the tagged result.</summary>
    public string? CReturns { get; init; }

    /// <summary>
    /// The tags the slot's <c>@return</c> block declared. A return is not a parameter and
    /// cannot carry one of its own, so what the returned value means beyond its type —
    /// that it is the front of a sequence, say — has nowhere else to be stated.
    /// </summary>
    public IReadOnlyList<string> ReturnTags { get; init; } = [];

    /// <summary>
    /// The type of the parameter the slot hangs off, which is not always the struct that
    /// declares it: an owner wrapper's <c>destroy</c> takes the value the wrapper holds,
    /// not the wrapper. A writer that declares the ABI itself instead of importing the
    /// header has no other source for it, and a declaration naming the wrong pointer type
    /// still compiles.
    /// </summary>
    public string? Receiver { get; init; }

    /// <summary>The native method a function is reached by; null for a slot of a vtable.</summary>
    public string? Symbol { get; init; }

    /// <summary>Whether the slot carries <paramref name="tag"/>, with or without a value.</summary>
    public bool Has(string tag) => Tags.Any(t => t == tag || t.StartsWith(tag + ":"));

    /// <summary>
    /// The text after the colon of the first tag named <paramref name="tag"/>, or null.
    /// </summary>
    public string? TagValue(string tag) => Tags.FirstOrDefault(t => t.StartsWith(tag + ":"))?[(tag.Length + 1)..];

    /// <summary>The value of <paramref name="tag"/> on the slot's return, or null.</summary>
    public string? ReturnTagValue(string tag) =>
        ReturnTags.FirstOrDefault(t => t.StartsWith(tag + ":"))?[(tag.Length + 1)..];
}

/// <summary>
/// One named value of an enum, with its value as text and whether that value is an integer.
/// </summary>
public record ApiEnumValue(string Name, string RawValue, bool IsInt, string? Doc);

/// <summary>
/// An enum the header declares, with its values in the order the description lists them.
/// </summary>
public record ApiEnum(string Name, string? Doc, IReadOnlyList<ApiEnumValue> Values)
{
    /// <summary>The tags the enum's own doc block declared.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Whether the enum carries <paramref name="tag"/>, with or without a value.</summary>
    public bool Has(string tag) => Tags.Any(t => t == tag || t.StartsWith(tag + ":"));

    /// <summary>
    /// Whether this enum came from a header this domain composes against rather than
    /// describes (<c>--compose</c>). Present because a composed struct's field can be
    /// typed by it and a writer must name that type; the owning domain still emits it.
    /// </summary>
    public bool External { get; init; }
}

/// <summary>A field of a struct, with its type, tags and doc as declared.</summary>
public record ApiField(string Name, string Type, IReadOnlyList<string> Tags, string? Doc)
{
    /// <summary>The width of a bit-field, or null for any other field.</summary>
    public int? Bits { get; init; }

    /// <summary>
    /// The record the field is declared with when its type has no name of its own: a member
    /// union or struct written inside the declaration, or an anonymous one, whose name is then
    /// <c>unnamed_</c> and its position among the anonymous members of the struct.
    /// </summary>
    public ApiStruct? Inline { get; init; }

    /// <summary>
    /// Whether the field carries <paramref name="tag"/>, with or without a value.
    /// </summary>
    public bool Has(string tag) => Tags.Any(t => t == tag || t.StartsWith(tag + ":"));

    /// <summary>
    /// The text after the colon of the first tag named <paramref name="tag"/>, or null.
    /// </summary>
    public string? TagValue(string tag) => Tags.FirstOrDefault(t => t.StartsWith(tag + ":"))?[(tag.Length + 1)..];
}

/// <summary>
/// A struct or vtable the header declares: its fields, and the slots of a vtable.
/// </summary>
public record ApiStruct(string Name, string? Doc, IReadOnlyList<string> Tags, IReadOnlyList<ApiField> Fields, IReadOnlyList<ApiSlot> Slots)
{
    /// <summary>
    /// Whether the struct holds any function-pointer slot. This is a shape
    /// question, not a role one: a parameter bag also carries the callback it
    /// registers, so a caller deciding whether to emit an interface must
    /// additionally consult the naming convention (see <c>Convention.IsParamsType</c>).
    /// </summary>
    public bool IsVtable => Slots.Count > 0;

    /// <summary>
    /// Whether this struct came from a header this domain composes against rather than
    /// describes (<c>--compose</c>). It is present so a node in this domain can reference
    /// a bundle another domain owns and have its component set resolved; the domain still
    /// emits nothing for it, because the owning domain already does.
    /// </summary>
    public bool External { get; init; }

    /// <summary>Whether the record is a union: every member starts at offset zero.</summary>
    public bool IsUnion { get; init; }

    /// <summary>
    /// The names of the fields and slots in declaration order, or <see langword="null"/> when
    /// every field precedes every slot. Layout follows declaration order, which the two lists
    /// alone do not keep.
    /// </summary>
    public IReadOnlyList<string>? Order { get; init; }

    /// <summary>
    /// Whether the struct carries <paramref name="tag"/>, with or without a value.
    /// </summary>
    public bool Has(string tag) => Tags.Any(t => t == tag || t.StartsWith(tag + ":"));

    /// <summary>
    /// The text after the colon of the first tag named <paramref name="tag"/>, or null.
    /// </summary>
    public string? TagValue(string tag) => Tags.FirstOrDefault(t => t.StartsWith(tag + ":"))?[(tag.Length + 1)..];
}

/// <summary>An exported global the header declares (<c>extern const T NAME;</c>).</summary>
public record ApiVariable(string Name, string Type, string? Doc);

/// <summary>
/// An object-like macro the header defines as a constant. <see cref="Type"/> is the C type the
/// expression has; <see cref="Value"/> is the decimal value of an integer or the literal of a
/// string, and is null for a record, whose members <see cref="Fields"/> lists in order.
/// </summary>
public record ApiConstant(string Name, string Type, string? Value, IReadOnlyList<ApiConstantField>? Fields, string? Doc);

/// <summary>One member of a record constant: its name and its decimal value.</summary>
public record ApiConstantField(string Name, string Value);

/// <summary>What a value of an initializer is.</summary>
public enum ApiValueKind { Number, String, Name, OffsetOf, SizeOf, List }

/// <summary>
/// One value of an initializer. <see cref="Text"/> is a literal, the name a value refers to, the
/// <c>type, member</c> of an offsetof or the type of a sizeof; a list holds <see cref="Items"/>.
/// </summary>
public record ApiValue(ApiValueKind Kind, string Text, IReadOnlyList<ApiValue> Items);

/// <summary>
/// A variable the headers define with an initializer: its name, its C type without const, whether
/// it is const, and its value.
/// </summary>
public record ApiData(string Name, string Type, bool ReadOnly, ApiValue Value);

/// <summary>A function the header declares, with its return type and its parameters.</summary>
public record ApiFunction(string Name, string Returns, string? Doc, string? ReturnDoc, IReadOnlyList<ApiParam> Params)
{
    /// <summary>The tags of the function's own documentation.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Whether the function carries <paramref name="tag"/>.</summary>
    public bool Has(string tag) => Tags.Contains(tag);

    /// <summary>The return type C declares, where <c>Returns</c> holds the tagged result.</summary>
    public string? CReturns { get; init; }

    /// <summary>The tags the function's <c>@return</c> block declared.</summary>
    public IReadOnlyList<string> ReturnTags { get; init; } = [];
}

/// <summary>
/// A function-pointer typedef described lane by lane. Which lane carries the caller's
/// opaque context, which carries a utf8 string, and which is an outbound error channel
/// is a contract the typedef's own parameter documentation declares; the string
/// spelling of the type carries none of it, and a writer that has to guess gets it
/// wrong. Lanes keep the order the prototype declares, so an index identifies one.
/// </summary>
public record ApiCallback(string Name, string Returns, string? Doc, IReadOnlyList<ApiParam> Lanes)
{
    /// <summary>The tags of the typedef's own documentation, such as <c>bridge</c>.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Whether the typedef's documentation carries <paramref name="tag"/>.</summary>
    public bool Has(string tag) => Tags.Contains(tag);
}

/// <summary>
/// The declarations one header description lists, kept in the order they were read: enums,
/// structs and vtables, functions and variables, with the callbacks and type aliases beside them.
/// </summary>
public class ApiModel
{
    /// <summary>Every enum read, in the order the description lists them.</summary>
    public List<ApiEnum> Enums { get; } = [];

    /// <summary>
    /// Every struct and every vtable read, in the order the description lists them. A vtable's
    /// slots are on its <see cref="ApiStruct"/>.
    /// </summary>
    public List<ApiStruct> Structs { get; } = [];

    /// <summary>Every function read, in the order the description lists them.</summary>
    public List<ApiFunction> Functions { get; } = [];

    /// <summary>Every exported global read, in the order the description lists them.</summary>
    public List<ApiVariable> Variables { get; } = [];

    /// <summary>
    /// The records declared and never defined: handles a caller only ever holds the address of.
    /// </summary>
    public List<string> Opaques { get; } = [];

    /// <summary>The constants the headers define as macros, in the order they define them.</summary>
    public List<ApiConstant> Constants { get; } = [];

    /// <summary>The variables the headers define with an initializer, in definition order.</summary>
    public List<ApiData> Data { get; } = [];

    /// <summary>
    /// Function-pointer typedefs, keyed by the name a parameter is declared with.
    /// <see cref="TypeAliases"/> also holds each one, spelled as a type; this holds the
    /// description a projection needs to name the lanes.
    /// </summary>
    public List<ApiCallback> Callbacks { get; } = [];

    /// <summary>Scalar typedefs tagged <c>[id]</c>, each projected as a type of its own.</summary>
    public HashSet<string> IdTypes { get; } = [];

    /// <summary>The identifiers this model reaches from another domain.</summary>
    public HashSet<string> ForeignIdTypes { get; } = [];

    /// <summary>
    /// The callback a parameter's type names, or <see langword="null"/> if it names none.
    /// </summary>
    public ApiCallback? CallbackOf(string type) => Callbacks.FirstOrDefault(c => c.Name == type.Trim());

    /// <summary>The callback a parameter carries, inline or through a typedef.</summary>
    public ApiCallback? CallbackOf(ApiParam p) => CallbackOf(p.Callback ?? p.Type);

    /// <summary>
    /// Typedefs that resolve to a plain C primitive (<c>ke_entity</c> -&gt; <c>uint64_t</c>),
    /// which every writer must see through — a language with no typedef concept has
    /// nothing to map <c>ke_entity</c> onto otherwise. Typedefs naming a struct or enum
    /// are absent: those are real types the description already carries.
    /// </summary>
    public Dictionary<string, string> TypeAliases { get; } = [];

    /// <summary>
    /// The handle a type is, or null: an opaque record behind one pointer more than
    /// <paramref name="outer"/>, or a typedef of a pointer behind <paramref name="outer"/>.
    /// </summary>
    public string? HandleOf(string type, int outer = 0)
    {
        var parsed = CType.Of(type);
        if (Opaques.Contains(parsed.Name) && parsed.Pointers == outer + 1) return parsed.Name;
        return parsed.Pointers == outer && TypeAliases.TryGetValue(parsed.Name, out var target) && target.EndsWith('*') && !target.Contains('(') ? parsed.Name : null;
    }

    /// <summary>The <c>[owner]</c> struct that holds <paramref name="vtable"/>, or null. Fails when two do.</summary>
    public ApiStruct? OwnerOf(string vtable)
    {
        var owners = Structs.Where(s => s.Has("owner") && s.Fields.Any(f => Bare(f.Type) == vtable)).ToList();
        return owners.Count > 1
            ? throw new InvalidOperationException($"{vtable} has two owners: {string.Join(", ", owners.Select(o => o.Name))}")
            : owners.FirstOrDefault();
    }

    /// <summary>The function that makes the owner of <paramref name="vtable"/>, or null. Fails when two do.</summary>
    public ApiFunction? FactoryOf(string vtable)
    {
        if (OwnerOf(vtable) is not { } owner) return null;
        var factories = Functions.Where(f => Makes(f) == owner.Name).ToList();
        return factories.Count > 1
            ? throw new InvalidOperationException($"{owner.Name} has two factories: {string.Join(", ", factories.Select(f => f.Name))}")
            : factories.FirstOrDefault();
    }

    /// <summary>The <c>[owner]</c> struct <paramref name="function"/> returns or writes through a last <c>[out]</c>, or null.</summary>
    public string? Makes(ApiFunction function)
    {
        var made = function.Params.Count > 0 && function.Params[^1].Has("out") ? Bare(function.Params[^1].Type) : CTypes.IsPointer(function.Returns) ? "" : Bare(function.CReturns ?? function.Returns);
        return Structs.Any(s => s.Name == made && s.Has("owner")) ? made : null;
    }

    static string Bare(string type) => CType.Of(type).Name;

    /// <summary>
    /// Resolves a type through <see cref="TypeAliases"/> until it is no longer an alias.
    /// </summary>
    public string ResolveAlias(string type)
    {
        var t = type.Trim();
        for (var i = 0; i < 8 && !IdTypes.Contains(t) && !ForeignIdTypes.Contains(t) && TypeAliases.TryGetValue(t, out var next); i++) t = next;
        return t;
    }
}

/// <summary>Reads the JSON description of one header into an <see cref="ApiModel"/>.</summary>
public static class ApiReader
{
    /// <summary>
    /// Reads the root object of a header description into a new model. Throws when a required key
    /// is missing, when a value has the wrong JSON kind, or when a parameter has no <c>tags</c>
    /// key.
    /// </summary>
    /// <param name="root">
    /// The root object of the description, whose keys <c>enums</c>, <c>structs</c>, <c>vtables</c>
    /// and <c>functions</c> are required.
    /// </param>
    /// <returns>The model holding every declaration the description lists.</returns>
    public static ApiModel Read(System.Text.Json.Nodes.JsonObject root)
    {
        var m = new ApiModel();
        foreach (var eRaw in root["enums"]!.AsArray())
        {
            var e = eRaw!.AsObject();
            m.Enums.Add(new ApiEnum(Str(e, "name")!, Str(e, "doc"),
                e["values"]!.AsArray().Select(vRaw =>
                {
                    var v = vRaw!.AsObject();
                    var isInt = v["value"] is System.Text.Json.Nodes.JsonValue jv && jv.TryGetValue<long>(out _);
                    return new ApiEnumValue(Str(v, "name")!,
                        isInt ? v["value"]!.GetValue<long>().ToString() : Str(v, "value")!,
                        isInt, Str(v, "doc"));
                }).ToList())
            {
                External = e["external"]?.GetValue<bool>() == true,
                Tags = e["tags"]?.AsArray().Select(t => t!.GetValue<string>()).ToList() ?? [],
            });
        }

        static ApiStruct ReadRecord(System.Text.Json.Nodes.JsonObject o, bool vtable)
        {
            var slots = vtable
                ? o["slots"]!.AsArray().Select(s => ReadSlot(s!.AsObject())).ToList()
                : [];
            var fields = o["fields"]!.AsArray()
                .Select(fRaw =>
                {
                    var f = fRaw!.AsObject();
                    return new ApiField(Str(f, "name")!, Str(f, "type")!,
                        WithText(Str(f, "type")!, f["tags"]?.AsArray().Select(t => t!.GetValue<string>()).ToList() ?? []), Str(f, "doc"))
                    {
                        Inline = f["inline"] is System.Text.Json.Nodes.JsonObject inline
                            ? ReadRecord(inline, inline["slots"] is not null) : null,
                        Bits = f["bits"]?.GetValue<int>(),
                    };
                })
                .ToList();
            var tags = o["tags"]?.AsArray().Select(t => t!.GetValue<string>()).ToList() ?? [];
            return new ApiStruct(Str(o, "name")!, Str(o, "doc"), tags, fields, slots)
            {
                External = o["external"]?.GetValue<bool>() == true,
                IsUnion = o["union"]?.GetValue<bool>() == true,
                Order = o["order"]?.AsArray().Select(n => n!.GetValue<string>()).ToList(),
            };
        }

        foreach (var s in root["structs"]!.AsArray()) m.Structs.Add(ReadRecord(s!.AsObject(), vtable: false));
        foreach (var v in root["vtables"]!.AsArray()) m.Structs.Add(ReadRecord(v!.AsObject(), vtable: true));

        foreach (var o in root["opaques"]?.AsArray() ?? []) m.Opaques.Add(o!.GetValue<string>());

        foreach (var n in root["data"]?.AsArray() ?? [])
        {
            var o = n!.AsObject();
            m.Data.Add(new ApiData(Str(o, "name")!, Str(o, "type")!, o["readonly"]!.GetValue<bool>(), ReadValue(o["value"]!.AsObject())));
        }

        foreach (var c in root["constants"]?.AsArray() ?? [])
        {
            var o = c!.AsObject();
            m.Constants.Add(new ApiConstant(Str(o, "name")!, Str(o, "type")!, Str(o, "value"),
                o["fields"]?.AsArray().Select(f => new ApiConstantField(Str(f!.AsObject(), "name")!, Str(f.AsObject(), "value")!)).ToList(),
                Str(o, "doc")));
        }

        if (root["type_aliases"] is System.Text.Json.Nodes.JsonObject aliases)
            foreach (var kv in aliases)
                m.TypeAliases[kv.Key] = kv.Value!.GetValue<string>();

        foreach (var c in root["callbacks"]?.AsArray() ?? [])
        {
            var o = c!.AsObject();
            m.Callbacks.Add(new ApiCallback(Str(o, "name")!, Str(o, "returns")!, Str(o, "doc"),
                o["lanes"]!.AsArray().Select(p => ReadParam(p!.AsObject())).ToList())
            {
                Tags = (o["tags"]?.AsArray() ?? []).Select(t => t!.GetValue<string>()).ToList(),
            });
        }

        foreach (var f in root["functions"]!.AsArray())
        {
            var o = f!.AsObject();
            m.Functions.Add(new ApiFunction(Str(o, "name")!, Returned(o), Str(o, "doc"), Str(o, "return_doc"),
                o["params"]!.AsArray().Select(p => ReadParam(p!.AsObject())).ToList())
            {
                ReturnTags = ReadTags(o, "return_tags"),
                CReturns = Str(o, "returns"),
                Tags = ReadTags(o, "tags"),
            });
        }
        foreach (var id in root["id_types"]?.AsArray() ?? []) m.IdTypes.Add(id!.GetValue<string>());
        foreach (var id in root["foreign_id_types"]?.AsArray() ?? []) m.ForeignIdTypes.Add(id!.GetValue<string>());
        foreach (var v in root["variables"]?.AsArray() ?? [])
        {
            var o = v!.AsObject();
            m.Variables.Add(new ApiVariable(Str(o, "name")!, Str(o, "type")!, Str(o, "doc")));
        }

        return m;
    }

    static ApiValue ReadValue(System.Text.Json.Nodes.JsonObject o) =>
        new(Enum.Parse<ApiValueKind>(Str(o, "kind")!), Str(o, "text") ?? "",
            o["items"]?.AsArray().Select(i => ReadValue(i!.AsObject())).ToList() ?? []);

    static string Returned(System.Text.Json.Nodes.JsonObject o) =>
        ReadTags(o, "return_tags").Contains("result") ? Convention.TaggedResult : Str(o, "returns")!;

    static ApiSlot ReadSlot(System.Text.Json.Nodes.JsonObject o) => new(Str(o, "name")!, Returned(o),
        o["tags"]?.AsArray().Select(t => t!.GetValue<string>()).ToList() ?? [], Str(o, "doc"),
        Str(o, "return_doc"), o["params"]!.AsArray().Select(p => ReadParam(p!.AsObject())).ToList())
    {
        ReturnTags = ReadTags(o, "return_tags"),
        CReturns = Str(o, "returns"),
        Receiver = Str(o, "receiver"),
    };

    static List<string> ReadTags(System.Text.Json.Nodes.JsonObject o, string key) =>
        o[key]?.AsArray().Select(t => t!.GetValue<string>()).ToList() ?? [];

    static ApiParam ReadParam(System.Text.Json.Nodes.JsonObject o) => new(Str(o, "name"), Str(o, "type")!,
        WithText(Str(o, "type")!, o["tags"]!.AsArray().Select(t => t!.GetValue<string>()).ToList()), Str(o, "doc")) { Callback = Str(o, "callback") };

    static List<string> WithText(string type, List<string> tags)
    {
        if (CType.Of(type) is { Name: "char", Const: true, Pointers: 1 } && !tags.Contains("utf8")) tags.Add("utf8");
        return tags;
    }

    static string? Str(System.Text.Json.Nodes.JsonObject o, string key) => o[key]?.GetValue<string>();
}
