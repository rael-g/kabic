namespace Kabic;

/// <summary>
/// One result code the ABI names on the managed side: the constant, and the exception a caller
/// catches for it, declared by kabic as deriving from <paramref name="Extends"/> when that is given.
/// </summary>
public sealed record ResultCodeSpec(string Code, string Managed, string? Extends);

/// <summary>
/// The naming and shape conventions of the C ABI being compiled: everything <c>kabic</c> knows
/// about its consumer that is not intrinsic to C itself.
/// </summary>
public sealed class Convention
{
    /// <summary>
    /// Prefix every public symbol in this ABI carries (<c>ke_input</c>, <c>ke_logger_create</c>).
    /// </summary>
    public required string SymbolPrefix { get; init; }

    /// <summary>
    /// Suffix marking an owner-wrapper struct (<c>ke_input_handle{ref, destroy}</c>) — a plain
    /// lifetime holder, not a vtable a caller registers a provider or callback for.
    /// </summary>
    public required string HandleSuffix { get; init; }

    /// <summary>
    /// Suffix marking a factory function for the vtable it is named after (<c>ke_input_create</c>).
    /// </summary>
    public required string FactorySuffix { get; init; }

    /// <summary>
    /// Suffix marking a parameter bag (<c>ke_runtime_system_params</c>) — a value the caller
    /// fills in and passes by pointer to one call, never an interface. Holding a function
    /// pointer does not make it one: a registration bag carries the callback being registered
    /// (<c>execute</c>, <c>on_load</c>) right next to its plain data fields, so slot count
    /// alone cannot tell the two apart, and mistaking a bag for a vtable emits a wrapper class
    /// with <c>Borrow</c>/<c>Dispose</c> around what is not an object. The ABI guarantees this
    /// suffix for every parameter bag, which is what makes it a sound discriminator.
    /// </summary>
    public required string ParamsSuffix { get; init; }

    /// <summary>Whether a struct name marks a parameter bag rather than an interface.</summary>
    public bool IsParamsType(string structName) => structName.EndsWith(ParamsSuffix);

    /// <summary>Boolean return-type spellings that mean "succeeded".</summary>
    public IReadOnlyList<string> BooleanReturnTypes => ["_Bool", "bool"];

    /// <summary>
    /// True if <paramref name="structName"/> names an owner-wrapper rather than a real vtable.
    /// </summary>
    public bool IsHandleType(string structName) => structName.EndsWith(HandleSuffix);

    /// <summary>
    /// The owner-wrapper struct name paired with a given vtable (<c>ke_window</c> →
    /// <c>ke_window_handle</c>).
    /// </summary>
    public string HandleTypeFor(string vtableName) => vtableName + HandleSuffix;

    /// <summary>
    /// The factory function name paired with a given vtable (<c>ke_input</c> →
    /// <c>ke_input_create</c>).
    /// </summary>
    public string FactoryNameFor(string vtableName) => vtableName + FactorySuffix;

    /// <summary>
    /// True if <paramref name="functionName"/> is spelled as a factory for some vtable.
    /// </summary>
    public bool IsFactoryName(string functionName) => functionName.EndsWith(FactorySuffix);

    /// <summary>
    /// True if a slot returns the result code (<see cref="ResultType"/>): it fails by returning a
    /// code other than <see cref="ResultOk"/>, and hands its value back through an out-parameter.
    /// </summary>
    public bool ReturnsResult(string returns) => ResultType.Length > 0 && returns.Trim() == ResultType;

    /// <summary>
    /// The owner-wrapper handle a factory returning <see cref="ResultType"/> writes through its last
    /// parameter, or null when <paramref name="factory"/> is not of that shape.
    /// </summary>
    public string? ResultFactoryHandle(ApiFunction factory) =>
        ReturnsResult(factory.Returns) && factory.Params.Count > 0
            && CTypes.IsPointer(factory.Params[^1].Type)
            && CTypes.Deref(factory.Params[^1].Type).Replace("struct ", "").Trim() is var target
            && IsHandleType(target)
            ? target : null;

    /// <summary>
    /// The parameters a caller of <paramref name="factory"/> supplies: all of them, but the handle
    /// written back when the factory returns <see cref="ResultType"/>.
    /// </summary>
    public IReadOnlyList<ApiParam> FactoryInputs(ApiFunction factory) =>
        ResultFactoryHandle(factory) is not null
            ? factory.Params.Take(factory.Params.Count - 1).ToList()
            : factory.Params.ToList();

    /// <summary>The C type a fallible call returns its outcome as (<c>ke_result</c>).</summary>
    public string ResultType { get; init; } = "";

    /// <summary>The constant of <see cref="ResultType"/> that means success (<c>KE_OK</c>).</summary>
    public string ResultOk { get; init; } = "";

    /// <summary>The fully qualified managed type the bindings declare for <see cref="ResultType"/>.</summary>
    public string ResultManagedType { get; init; } = "";

    /// <summary>
    /// The fully qualified managed class that turns a code into an exception
    /// (<c>ThrowIfFailed</c>, <c>FromCode</c>).
    /// </summary>
    public string ResultHelperClass { get; init; } = "";

    /// <summary>
    /// Path, relative to the repository root, of the generated file that maps each result code to
    /// its exception.
    /// </summary>
    public string ResultCodesOut { get; init; } = "";

    /// <summary>The codes whose managed exception the ABI names, in declaration order.</summary>
    public IReadOnlyList<ResultCodeSpec> ResultCodes { get; init; } = [];

    /// <summary>
    /// True if this slot signals failure through what it returns: a boolean, or the result code.
    /// </summary>
    public bool SignalsFailureByReturn(string returns) => BooleanReturnTypes.Contains(returns) || ReturnsResult(returns);

    /// <summary>
    /// Explicit target-language type names for ABI symbols whose derived name is
    /// unusable — chiefly a vtable whose name matches its own namespace's last
    /// segment (<c>ke_ecs</c> in <c>KernelEngine.Ecs</c> would derive to
    /// <c>Ecs</c>, forcing every reference to be fully qualified).
    /// </summary>
    public IReadOnlyDictionary<string, string> TypeNameOverrides { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Strips <see cref="SymbolPrefix"/> from a symbol, leaving the rest untouched.
    /// </summary>
    public string StripPrefix(string name) =>
        name.StartsWith(SymbolPrefix) ? name[SymbolPrefix.Length..] : name;

    /// <summary>
    /// How each language rewrites a name, by language: regular expressions in order, the first
    /// that matches applying. A rule carries no meaning; it is a spelling.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<(System.Text.RegularExpressions.Regex Pattern, string Replacement)>> NameRules { get; init; } =
        new Dictionary<string, IReadOnlyList<(System.Text.RegularExpressions.Regex, string)>>();

    /// <summary><paramref name="name"/> rewritten by the first rule of <paramref name="language"/> that matches.</summary>
    public string Spell(string language, string name)
    {
        foreach (var (pattern, replacement) in NameRules.GetValueOrDefault(language) ?? [])
            if (pattern.IsMatch(name)) return pattern.Replace(name, replacement);
        return name;
    }

    /// <summary>
    /// Name of the directory under which a public header lives
    /// (<c>kernel_engine/audio/audio.h</c>), so a header is addressed without its install prefix.
    /// </summary>
    public string IncludeDirectoryName { get; init; } = "kernel_engine";

    /// <summary>
    /// Namespace of the raw bindings every other binding assembly takes its shared types from.
    /// </summary>
    public string CommonBindingsNamespace { get; init; } = "";

    /// <summary>Class that holds the raw P/Invoke methods of a binding assembly.</summary>
    public string BindingsMethodsClass { get; init; } = "NativeMethods";


    /// <summary>
    /// Header, relative to the repository root, that declares the result type and its codes.
    /// </summary>
    public string ResultAbiHeader { get; init; } = "";

    /// <summary>
    /// Include directories the result header is read with, relative to the repository root.
    /// </summary>
    public IReadOnlyList<string> ResultAbiIncludeDirs { get; init; } = [];

    /// <summary>
    /// Managed namespace the generated projections import for the shared types, beside the bindings
    /// namespace.
    /// </summary>
    public string CommonManagedNamespace { get; init; } = "";

    /// <summary>C owner-wrapper handle types and the managed type a caller holds instead.</summary>
    public IReadOnlyDictionary<string, string> HandleTypes { get; init; } = new Dictionary<string, string>();

    /// <summary>The packs of types this ABI has beyond what C itself provides.</summary>
    public IReadOnlyList<IShapePack> Packs { get; init; } = [];

    /// <summary>
    /// The shape a C type is, as the first pack that recognises it says, or null.
    /// </summary>
    public (IShapePack Pack, TypeShape Shape)? ShapeOf(ApiModel model, string cType)
    {
        foreach (var pack in Packs)
            if (pack.Recognize(model, cType) is { } shape)
                return (pack, shape);
        return null;
    }

    /// <summary>
    /// How <paramref name="language"/> spells the shape of a C type, or null when it is no shape or
    /// the language has no such type.
    /// </summary>
    public string? Project(string language, ApiModel model, string cType) =>
        ShapeOf(model, cType) is { } found ? found.Pack.Project(language, found.Shape) : null;

    /// <summary>The group a parameter opens, with the pack that groups it.</summary>
    public (IShapePack Pack, ParamGroup Group)? GroupOf(ApiParam parameter)
    {
        foreach (var pack in Packs)
            if (pack.GroupOf(parameter) is { } group)
                return (pack, group);
        return null;
    }

    /// <summary>Everything the packs say <paramref name="language"/> has to import.</summary>
    public IEnumerable<string> Imports(string language) => Packs.SelectMany(p => p.Imports(language)).Distinct();

    /// <summary>
    /// Every library, beyond the language's own, the packs say <paramref name="language"/> has to
    /// reference.
    /// </summary>
    public IEnumerable<string> Libraries(string language) => Packs.SelectMany(p => p.Libraries(language)).Distinct();

    /// <summary>
    /// Reads the <c>convention</c> object of a manifest into a <see cref="ConventionBuilder"/> and
    /// builds it; <paramref name="pack"/> resolves the name of a pack.
    /// </summary>
    public static Convention FromJson(System.Text.Json.Nodes.JsonObject json, Func<string, IShapePack> pack)
    {
        static string Text(System.Text.Json.Nodes.JsonObject o, string key) =>
            o[key]?.GetValue<string>() ?? throw new InvalidOperationException($"the convention declares no '{key}'");
        static string Or(System.Text.Json.Nodes.JsonObject o, string key, string fallback) => o[key]?.GetValue<string>() ?? fallback;

        var builder = new ConventionBuilder()
            .Symbols(Text(json, "symbolPrefix"), Text(json, "handleSuffix"), Text(json, "factorySuffix"),
                Text(json, "paramsSuffix"))
            .Bindings(Or(json, "includeDirectoryName", "kernel_engine"), Or(json, "commonBindingsNamespace", ""),
                Or(json, "bindingsMethodsClass", "NativeMethods"))
            .Managed(Text(json, "commonManagedNamespace"))
            .ResultAbi(Or(json, "resultAbiHeader", ""),
                (json["resultAbiIncludeDirs"]?.AsArray() ?? []).Select(n => n!.GetValue<string>()));
        if (json["resultType"] is not null)
            builder.Result(Text(json, "resultType"), Text(json, "resultOk"), Text(json, "resultManagedType"),
                Text(json, "resultHelperClass"), Or(json, "resultCodesOut", ""));
        foreach (var r in json["resultCodes"]?.AsArray() ?? [])
            builder.AddResultCode(r!["code"]!.GetValue<string>(), r["managed"]!.GetValue<string>(),
                r["extends"]?.GetValue<string>());

        foreach (var (cType, managed) in json["handleTypes"]?.AsObject() ?? [])
            builder.AddHandleType(cType, managed!.GetValue<string>());
        foreach (var (symbol, name) in json["typeNameOverrides"]?.AsObject() ?? [])
            builder.AddTypeName(symbol, name!.GetValue<string>());
        foreach (var (language, rules) in json["names"]?.AsObject() ?? [])
            foreach (var rule in rules!.AsArray())
                builder.AddNameRule(language, rule!["match"]!.GetValue<string>(), rule["replace"]!.GetValue<string>());
        foreach (var name in json["packs"]?.AsArray() ?? [])
            builder.Use(pack(name!.GetValue<string>()));
        return builder.Build();
    }
}
