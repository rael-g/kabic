namespace Kabic;

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

    /// <summary>Boolean return-type spellings that mean "succeeded".</summary>
    public IReadOnlyList<string> BooleanReturnTypes => ["_Bool", "bool"];

    /// <summary>
    /// True if a slot returns the result code (<see cref="ResultType"/>): it fails by returning a
    /// code other than <see cref="ResultOk"/>, and hands its value back through an out-parameter.
    /// </summary>
    public bool ReturnsResult(string returns) =>
        returns == TaggedResult || (ResultType.Length > 0 && returns.Trim() == ResultType);

    /// <summary>The return of a call whose <c>@return</c> is tagged <c>[result]</c>.</summary>
    public const string TaggedResult = "[result]";

    /// <summary>The expression that means success.</summary>
    public string ResultOkValue => char.IsDigit(ResultOk[0]) ? ResultOk : $"{ResultManagedType}.{ResultOk}";

    /// <summary>
    /// The parameters a caller of <paramref name="factory"/> supplies: all of them, but the owner
    /// written back when the factory returns <see cref="ResultType"/>.
    /// </summary>
    public IReadOnlyList<ApiParam> FactoryInputs(ApiModel model, ApiFunction factory) =>
        ReturnsResult(factory.Returns) && model.Makes(factory) is not null
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
    /// True if this slot signals failure through what it returns: a boolean, or the result code.
    /// </summary>
    public bool SignalsFailureByReturn(string returns) => BooleanReturnTypes.Contains(returns) || ReturnsResult(returns);

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

    /// <summary>The <c>vtable.slot</c> names the wrapper projects but a managed contract does not hold yet.</summary>
    public IReadOnlySet<string> Deferred { get; set; } = new HashSet<string>();

    /// <summary>The slots, as <c>vtable.slot</c>, that are projected nowhere yet.</summary>
    public IReadOnlySet<string> Unprojected { get; set; } = new HashSet<string>();

    /// <summary>Per language, the text of the configuration its formatter uses instead of the default.</summary>
    public IReadOnlyDictionary<string, string> FormatConfigs { get; set; } = new Dictionary<string, string>();

    /// <summary><paramref name="name"/> rewritten by the first rule of <paramref name="language"/> that matches.</summary>
    public string Spell(string language, string name)
    {
        foreach (var (pattern, replacement) in NameRules.GetValueOrDefault(language) ?? [])
            if (pattern.IsMatch(name)) return pattern.Replace(name, replacement);
        return name;
    }

    /// <summary>
    /// Name of the directory under which a public header lives
    /// (<c>mylib/audio/audio.h</c>), so a header is addressed without its install prefix.
    /// </summary>
    public string IncludeDirectoryName { get; init; } = "";

    /// <summary>
    /// Namespace of the raw bindings every other binding assembly takes its shared types from.
    /// </summary>
    public string CommonBindingsNamespace { get; init; } = "";

    /// <summary>Class that holds the raw P/Invoke methods of a binding assembly.</summary>
    public string BindingsMethodsClass { get; init; } = "NativeMethods";




    /// <summary>
    /// Managed namespace the generated projections import for the shared types, beside the bindings
    /// namespace.
    /// </summary>
    public string CommonManagedNamespace { get; init; } = "";

    /// <summary>
    /// The target type a C type is written as, by language: a spelling, whose layout is the
    /// declarer's to keep equal to the C type's.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> TypeMap { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>();

    /// <summary>How <paramref name="language"/> spells <paramref name="cType"/>, or null when the map holds no such type.</summary>
    public string? Project(string language, string cType) =>
        TypeMap.GetValueOrDefault(language)?.GetValueOrDefault(CTypes.Unqualified(cType));

    /// <summary>
    /// Reads the <c>convention</c> object of a manifest into a <see cref="ConventionBuilder"/> and
    /// builds it.
    /// </summary>
    public static Convention FromJson(System.Text.Json.Nodes.JsonObject json)
    {
        static string Text(System.Text.Json.Nodes.JsonObject o, string key) =>
            o[key]?.GetValue<string>() ?? throw new InvalidOperationException($"the convention declares no '{key}'");
        static string Or(System.Text.Json.Nodes.JsonObject o, string key, string fallback) => o[key]?.GetValue<string>() ?? fallback;

        var builder = new ConventionBuilder()
            .Symbols(Text(json, "symbolPrefix"))
            .Bindings(Or(json, "includeDirectoryName", ""), Or(json, "commonBindingsNamespace", ""),
                Or(json, "bindingsMethodsClass", "NativeMethods"))
            .Managed(Text(json, "commonManagedNamespace"));
        if (json["resultType"] is not null)
            builder.Result(Text(json, "resultType"), Text(json, "resultOk"), Text(json, "resultManagedType"),
                Text(json, "resultHelperClass"));

        foreach (var (language, rules) in json["names"]?.AsObject() ?? [])
            foreach (var rule in rules!.AsArray())
                builder.AddNameRule(language, rule!["match"]!.GetValue<string>(), rule["replace"]!.GetValue<string>());
        foreach (var (language, types) in json["types"]?.AsObject() ?? [])
            foreach (var (cType, target) in types!.AsObject())
                builder.AddType(language, cType, target!.GetValue<string>());
        var convention = builder.Build();
        convention.Deferred = (json["deferred"]?.AsArray().Select(n => n!.GetValue<string>()) ?? []).ToHashSet();
        convention.Unprojected = (json["unprojected"]?.AsArray().Select(n => n!.GetValue<string>()) ?? []).ToHashSet();
        return convention;
    }
}
