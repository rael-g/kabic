namespace Kabic;

/// <summary>
/// Builds the <see cref="Convention"/> of an ABI by declaring what it names, one call per fact.
/// Every list is open: an ABI adds as many result codes, mapped types or handle types as it has.
/// </summary>
public sealed class ConventionBuilder
{
    string symbolPrefix = "";
    readonly Dictionary<string, List<(System.Text.RegularExpressions.Regex, string)>> nameRules = [];
    string includeDirectoryName = "", commonBindingsNamespace = "", bindingsMethodsClass = "NativeMethods";
    string commonManagedNamespace = "";
    string resultType = "", resultOk = "", resultManagedType = "", resultHelperClass = "";
    readonly Dictionary<string, string> handleTypes = [], typeNames = [];
    readonly Dictionary<string, Dictionary<string, string>> typeMap = [];

    /// <summary>
    /// The spellings that mark what a symbol is: its prefix.
    /// </summary>
    public ConventionBuilder Symbols(string prefix)
    {
        symbolPrefix = prefix;
        return this;
    }

    /// <summary>Appends a rule rewriting a name in <paramref name="language"/>.</summary>
    public ConventionBuilder AddNameRule(string language, string pattern, string replacement)
    {
        (nameRules.TryGetValue(language, out var rules) ? rules : nameRules[language] = []).Add((new(pattern), replacement));
        return this;
    }

    /// <summary>
    /// Where a header is addressed from, and the names the raw bindings are generated into.
    /// </summary>
    public ConventionBuilder Bindings(string includeDirectory, string commonNamespace, string methodsClass = "NativeMethods")
    {
        (includeDirectoryName, commonBindingsNamespace, bindingsMethodsClass) = (includeDirectory, commonNamespace, methodsClass);
        return this;
    }

    /// <summary>The managed namespace the projections import for the shared types.</summary>
    public ConventionBuilder Managed(string commonNamespace)
    {
        commonManagedNamespace = commonNamespace;
        return this;
    }

    /// <summary>
    /// How a fallible call returns its outcome as a code: the C type of the code, the constant that
    /// means success, the managed type the bindings declare for it, and the class that turns a code
    /// into an exception, the last two fully qualified.
    /// </summary>
    public ConventionBuilder Result(string cType, string ok, string managedType, string helperClass)
    {
        (resultType, resultOk, resultManagedType, resultHelperClass) = (cType, ok, managedType, helperClass);
        return this;
    }

    /// <summary>Spells <paramref name="cType"/> as <paramref name="target"/> in <paramref name="language"/>.</summary>
    public ConventionBuilder AddType(string language, string cType, string target)
    {
        (typeMap.TryGetValue(language, out var types) ? types : typeMap[language] = [])[cType] = target;
        return this;
    }

    /// <summary>
    /// A C owner-wrapper handle type and the managed type a caller holds instead.
    /// </summary>
    public ConventionBuilder AddHandleType(string cType, string managed)
    {
        handleTypes[cType] = managed;
        return this;
    }

    /// <summary>A target-language name for a symbol whose derived name is unusable.</summary>
    public ConventionBuilder AddTypeName(string symbol, string name)
    {
        typeNames[symbol] = name;
        return this;
    }

    /// <summary>
    /// Builds the <see cref="Convention"/> from the facts declared so far, without checking them.
    /// </summary>
    /// <returns>
    /// A convention holding a copy of the handle types, the type-name overrides, the result codes
    /// and the packs.
    /// </returns>
    public Convention Build() => new()
    {
        SymbolPrefix = symbolPrefix,
        NameRules = nameRules.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<(System.Text.RegularExpressions.Regex, string)>)kv.Value.ToList()),
        IncludeDirectoryName = includeDirectoryName,
        CommonBindingsNamespace = commonBindingsNamespace,
        BindingsMethodsClass = bindingsMethodsClass,
        CommonManagedNamespace = commonManagedNamespace,
        ResultType = resultType,
        ResultOk = resultOk,
        ResultManagedType = resultManagedType,
        ResultHelperClass = resultHelperClass,
        HandleTypes = new Dictionary<string, string>(handleTypes),
        TypeNameOverrides = new Dictionary<string, string>(typeNames),
        TypeMap = typeMap.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(kv.Value)),
    };
}
