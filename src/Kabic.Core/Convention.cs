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

    /// <summary>The C type a fallible call returns its outcome as (<c>ke_result</c>).</summary>
    public string ResultType { get; init; } = "";

    /// <summary>The constant of <see cref="ResultType"/> that means success (<c>KE_OK</c>).</summary>
    public string ResultOk { get; init; } = "";

    /// <summary>The fully qualified managed type the bindings declare for <see cref="ResultType"/>.</summary>
    public string ResultManagedType { get; init; } = "";

    /// <summary>
    /// The constant of <see cref="ResultType"/> a managed failure reports to native code when the
    /// exception carries no code of its own.
    /// </summary>
    public string ResultFailure { get; init; } = "";

    /// <summary><see cref="ResultFailure"/> spelled as a managed expression.</summary>
    public string ResultFailureValue => ResultFailure.Length > 0 && (char.IsDigit(ResultFailure[0]) || ResultFailure[0] == '-') ? ResultFailure : $"{ResultManagedType}.{ResultFailure}";

    /// <summary>
    /// The exception each constant of <see cref="ResultType"/> becomes, by its managed name, in the
    /// order the manifest lists them: the fully qualified type of an exception constructed over a
    /// message. A managed exception of exactly one of these types reports the first constant naming it.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> ResultExceptions { get; init; } = [];

    /// <summary>
    /// The fully qualified exception, constructed over a message, a constant with no entry in
    /// <see cref="ResultExceptions"/> becomes; when empty, an
    /// <see cref="System.Runtime.InteropServices.ExternalException"/> carrying the code.
    /// </summary>
    public string ResultGeneralException { get; init; } = "";

    /// <summary>
    /// The private class every generated class that makes a fallible call declares, turning a code
    /// into its exception and back.
    /// </summary>
    public const string ResultHelperClass = "NativeResult";

    /// <summary>The declaration of <see cref="ResultHelperClass"/>, indented as a member of a class.</summary>
    public IEnumerable<string> ResultHelperLines()
    {
        if (ResultManagedType.Length == 0) yield break;
        var t = ResultManagedType;
        yield return "";
        yield return "    /// <summary>Turns a result code into the exception that carries it, and back.</summary>";
        yield return $"    private static class {ResultHelperClass}";
        yield return "    {";
        yield return $"        public static void ThrowIfFailed({t} code, string call)";
        yield return "        {";
        yield return $"            if (code != {ResultOkValue}) throw FromCode(code, call);";
        yield return "        }";
        yield return "";
        yield return $"        public static global::System.Exception FromCode({t} code, string call)";
        yield return "        {";
        yield return "            var message = $\"{call} failed with {code}\";";
        yield return "            return code switch";
        yield return "            {";
        foreach (var (code, exception) in ResultExceptions)
            yield return $"                {Constant(code)} => new global::{exception}(message),";
        yield return ResultGeneralException.Length > 0
            ? $"                _ => new global::{ResultGeneralException}(message),"
            : "                _ => new global::System.Runtime.InteropServices.ExternalException(message, unchecked((int)code)),";
        yield return "            };";
        yield return "        }";
        yield return "";
        yield return $"        public static {t} ToCode(global::System.Exception exception)";
        yield return "        {";
        yield return $"            if (exception is global::System.Runtime.InteropServices.ExternalException native) return unchecked(({t})native.ErrorCode);";
        yield return "            var type = exception.GetType();";
        foreach (var (code, exception) in ResultExceptions.DistinctBy(e => e.Value))
            yield return $"            if (type == typeof(global::{exception})) return {Constant(code)};";
        yield return $"            return {ResultFailureValue};";
        yield return "        }";
        yield return "    }";

        string Constant(string name) => char.IsDigit(name[0]) || name[0] == '-' ? name : $"{t}.{name}";
    }




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
                Text(json, "resultFailure"), Or(json, "resultGeneralException", ""));
        foreach (var (code, exception) in json["resultExceptions"]?.AsObject() ?? [])
            builder.AddResultException(code, exception!.GetValue<string>());

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
