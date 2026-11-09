using System.Text;

using Kabic.Bindings;
using Kabic.CSharp;
using Kabic.Pipeline;

namespace Kabic.Factories;

/// <summary>
/// One factory of a bindings project, read from its header: the function, its config struct when
/// it takes one, the contract its handle belongs to, and the names the managed class is spelled with.
/// </summary>
public sealed record Factory(
    BindingJob Job,
    string ProjectDir,
    string ManagedNamespace,
    ApiFunction Function,
    ApiStruct? Config,
    DomainModel? Contract,
    string? ContractClass,
    string ClassName,
    string HandleType,
    IReadOnlyList<FactoryArgument> Arguments)
{
    /// <summary>The values the factory writes through an <c>[out]</c> before its handle, each a read-only property of the class.</summary>
    public IReadOnlyList<FactoryOutput> Outputs { get; init; } = [];
}

/// <summary>A value a factory writes through an <c>[out]</c> parameter besides its handle, as the property it becomes.</summary>
public sealed record FactoryOutput(string? Doc, string Property, string ManagedType);

/// <summary>
/// A dependency or a [utf8] string of a factory, or a field of its config struct, as the constructor argument it
/// becomes; <see cref="Field"/> is the config field it fills, null for a dependency.
/// </summary>
public sealed record FactoryArgument(string? Doc, string? Field, string Name, string ManagedType, string Assign, bool IsString)
{
    /// <summary>The C# literal the argument starts at when the caller leaves it out: its field's default, or null for zero.</summary>
    public string? Default { get; init; }

    /// <summary>The local the argument is gathered into when the factory takes it as a field of a struct of contracts, with that struct's type; null otherwise.</summary>
    public (string Local, string Type)? Bundle { get; init; }

    /// <summary>Whether the caller must give the argument: a required contract, or a value with no default that null cannot stand for.</summary>
    public bool IsRequired => IsRequiredProvider || (Default is null && !ManagedType.EndsWith('?') && !ManagedType.Contains(".INative"));

    /// <summary>Whether the argument is a contract the factory requires.</summary>
    public bool IsRequiredProvider => ManagedType.Contains(".INative") && !ManagedType.EndsWith('?');

    /// <summary>Whether the argument is a contract the factory takes when there is one.</summary>
    public bool IsOptionalProvider => ManagedType.Contains(".INative") && ManagedType.EndsWith('?');
}

/// <summary>
/// Projects the factory of each bindings project its manifest section names into a partial class
/// deriving from the class of the contract the factory's handle belongs to, constructed from the
/// factory's dependencies, then the fields of its config struct. Section: <c>{"bindings": ["&lt;binding name&gt;", ...]}</c>.
/// </summary>
public sealed class FactoriesExtension : IKabicExtension
{
    /// <inheritdoc/>
    public string Name => "factories";

    /// <summary>
    /// The factories of every bindings project the section of <paramref name="context"/> names. A
    /// factory that cannot be projected is reported through the context and left out.
    /// </summary>
    public static IReadOnlyList<Factory> Read(ExtensionContext context)
    {
        var names = context.Options?["bindings"]?.AsArray().Select(n => n!.GetValue<string>()).ToList() ?? [];
        if (names.Count == 0) return [];
        var jobs = BindingsPlanner.Plan(context.Root, context.ManifestPath).ToDictionary(j => j.Name);
        var factories = new List<Factory>();
        foreach (var name in names)
        {
            if (!jobs.TryGetValue(name, out var job))
            {
                context.Report($"factories: no bindings entry is called '{name}'");
                continue;
            }
            if (Of(context, job) is { } factory) factories.Add(factory);
        }
        return factories;
    }

    /// <inheritdoc/>
    public void Run(ExtensionContext context)
    {
        foreach (var f in Read(context))
            context.Emit(Path.Combine(Path.GetRelativePath(context.Root, f.ProjectDir), "Generated", $"{f.ClassName}.g.cs").Replace('\\', '/'), Render(f, context.Convention));
    }

    private static Factory? Of(ExtensionContext context, BindingJob job)
    {
        string Full(string p) => Path.GetFullPath(Path.Combine(job.NativeDir, p));
        var convention = context.Convention;
        var model = context.ReadHeaders(job.Traverse.Select(Full), job.IncludeDirectories.Select(Full), []);
        var functions = model.Functions.Where(f => model.Makes(f) is not null).ToList();
        if (functions.Count != 1)
        {
            context.Report($"factories: {job.Name} declares {functions.Count} factories; one is projected");
            return null;
        }
        var function = functions[0];
        if (function.Params is not [.., var outParam] || model.Makes(function) is null)
        {
            context.Report($"factories: {function.Name} does not end in a <handle>* out; it is not projected");
            return null;
        }
        var handleType = Bare(outParam.Type);
        var contractName = model.Structs.First(s => s.Name == handleType).Fields.Select(f => Bare(f.Type)).FirstOrDefault(n => context.Domains.Any(d => d.Model.Structs.Any(s => s.Name == n && s.IsVtable)));
        var contract = context.Domains.FirstOrDefault(d => d.Model.Structs.Any(s => s.Name == contractName && !s.External));
        if (contract is null && contractName is not null)
        {
            context.Report($"factories: {function.Name} makes a {contractName}, which no domain declares");
            return null;
        }
        var arguments = new List<FactoryArgument>();
        var outputs = new List<FactoryOutput>();
        ApiStruct? config = null;
        foreach (var param in function.Params.Take(function.Params.Count - 1))
        {
            if (param.Has("out"))
            {
                var pointee = CTypes.Unqualified(param.Type).TrimEnd('*', ' ');
                if ((context.Convention.Project(Languages.CSharp, pointee) ?? ValueStruct(context, pointee)) is not { } projected)
                {
                    context.Report($"factories: {function.Name}'s {param.Name} ({param.Type}) has no projection; it is not projected");
                    return null;
                }
                outputs.Add(new FactoryOutput(param.Doc, Idioms.Pascal(convention.StripPrefix(param.Name!).StartsWith("out_", StringComparison.Ordinal) ? param.Name![4..] : param.Name!), projected));
                continue;
            }
            if (outputs.Count > 0)
            {
                context.Report($"factories: {function.Name} takes {param.Name} after an out; it is not projected");
                return null;
            }
            if (config is not null)
            {
                context.Report($"factories: {function.Name} takes {param.Name} after its config; it is not projected");
                return null;
            }
            if (Dependency(context, param) is { } dependency)
                arguments.Add(dependency);
            else if (Bundle(context, param, model, job) is { } members)
                arguments.AddRange(members);
            else if (param.Has("utf8") && CTypes.Unqualified(param.Type) == "char *")
                arguments.Add(new FactoryArgument(param.Doc, null, Idioms.Ident(param.Name!), "string?", $"(sbyte*){Idioms.Ident(param.Name!)}Ptr", true));
            else if (model.Structs.FirstOrDefault(s => s.Name == Bare(param.Type)) is { } configStruct && param.Type.Contains('*'))
            {
                config = configStruct;
                foreach (var field in configStruct.Fields)
                {
                    if (Argument(context, field, job, model) is { } argument) arguments.Add(argument with { Default = DefaultLiteral(field, argument.ManagedType) });
                    else context.Report($"factories: {configStruct.Name}.{field.Name} ({field.Type}) has no projection");
                }
            }
            else
            {
                context.Report($"factories: {function.Name}'s {param.Name} ({param.Type}) is neither a contract nor a config; it is not projected");
                return null;
            }
        }
        var bareName = handleType.EndsWith("_handle", StringComparison.Ordinal) ? handleType[..^"_handle".Length] : handleType;
        var contractClass = contract is null ? "" : Idioms.TypeName(contractName!, convention);
        var stem = convention.Spell("factory", convention.StripPrefix(function.Name));
        var contractStem = convention.StripPrefix(contractName ?? bareName);
        var plugin = stem == contractStem ? "default" : stem.StartsWith(contractStem + "_", StringComparison.Ordinal) ? stem[(contractStem.Length + 1)..] : stem;
        var projectDir = Path.GetDirectoryName(job.NativeDir.TrimEnd('/', '\\'))!;
        var managedNamespace = job.Namespace.EndsWith(".Native", StringComparison.Ordinal) ? job.Namespace[..^".Native".Length] : job.Namespace;
        return new Factory(job, projectDir, managedNamespace, function, config, contract,
            contract is null ? null : $"{contract.Spec.Namespace}.{contractClass}",
            contract is null ? Idioms.TypeName(bareName, convention) : Idioms.Pascal(plugin) + contractClass, handleType, arguments) { Outputs = outputs };
    }

    private static FactoryArgument? Dependency(ExtensionContext context, ApiParam param)
    {
        var type = CTypes.Unqualified(param.Type);
        if (!type.EndsWith('*')) return null;
        var target = type.TrimEnd('*', ' ');
        var domain = context.Domains.FirstOrDefault(d => d.Model.Structs.Any(s => s.Name == target && !s.External && s.IsVtable));
        if (domain is null) return null;
        var name = Idioms.Ident(param.Name ?? target);
        var iface = $"{domain.Spec.Namespace}.{Idioms.NativeInterface(target, context.Convention)}";
        return param.Has("optional") || param.Has("nullable")
            ? new FactoryArgument(param.Doc, null, name, iface + "?", $"({name} is null ? null : {name}.Native)", false)
            : new FactoryArgument(param.Doc, null, name, iface, $"{name}.Native", false);
    }

    private static IReadOnlyList<FactoryArgument>? Bundle(ExtensionContext context, ApiParam param, ApiModel model, BindingJob job)
    {
        var type = CTypes.Unqualified(param.Type);
        if (!type.EndsWith('*')) return null;
        var bundle = model.Structs.FirstOrDefault(s => s.Name == type.TrimEnd('*', ' ') && !s.IsVtable);
        if (bundle is null || bundle.Fields.Count == 0) return null;
        var members = bundle.Fields.Select(field => Dependency(context, new ApiParam(field.Name, field.Type, field.Tags, field.Doc))).ToList();
        if (members.Any(m => m is null)) return null;
        var local = Idioms.Ident(param.Name ?? "bundle");
        var renamed = CSharpWriter.IsValue(bundle with { External = false });
        var managed = $"{job.Namespace}.{(renamed ? Idioms.TypeName(bundle.Name, context.Convention) : bundle.Name)}";
        return members.Zip(bundle.Fields, (m, field) => m! with
        {
            Field = null,
            Assign = $"{local}.{(renamed ? Idioms.Pascal(field.Name) : field.Name)} = {m!.Assign}",
            Bundle = (local, managed),
        }).ToList();
    }

    private static FactoryArgument? Argument(ExtensionContext context, ApiField field, BindingJob job, ApiModel model)
    {
        var name = Idioms.Ident(field.Name);
        var type = CTypes.Unqualified(field.Type);
        if (type.EndsWith('*') && context.Domains.Any(d => d.Model.Structs.Any(s => s.Name == type.TrimEnd('*', ' ') && s.IsVtable)))
        {
            var target = type.TrimEnd('*', ' ');
            var domain = context.Domains.FirstOrDefault(d => d.Model.Structs.Any(s => s.Name == target && !s.External));
            if (domain is null) return null;
            var iface = $"{domain.Spec.Namespace}.{Idioms.NativeInterface(target, context.Convention)}";
            return field.Has("optional") || field.Has("nullable")
                ? new FactoryArgument(field.Doc, field.Name, name, iface + "?", $"({name} is null ? null : {name}.Native)", false)
                : new FactoryArgument(field.Doc, field.Name, name, iface, $"{name}.Native", false);
        }
        if (field.Has("utf8") && type == "char *")
            return new FactoryArgument(field.Doc, field.Name, name, "string?", "", true);
        if (type.Contains('*')) return null;
        var owner = context.Domains.FirstOrDefault(d => d.Model.Enums.Any(e => e.Name == type));
        return new FactoryArgument(field.Doc, field.Name, name,
            context.Convention.Project(Languages.CSharp, type) is { } projected ? projected
            : ValueStruct(context, type) is { } value ? value
            : model.Enums.Any(e => e.Name == type && !e.External) ? $"{job.Namespace}.{Idioms.TypeName(type, context.Convention)}"
            : owner is null ? Idioms.CsPrimitive(type, context.Convention) : $"{owner.Spec.NativeNamespace[..^".Native".Length]}.{Idioms.TypeName(type, context.Convention)}", name, false);
    }

    private static string Bare(string cType) => CType.Of(cType).Name;

    private static string? ValueStruct(ExtensionContext context, string cType)
    {
        var name = CTypes.Unqualified(cType);
        var domain = context.Domains.FirstOrDefault(d => d.Model.Structs.Any(s => s.Name == name && !s.External && CSharpWriter.IsValue(s)));
        return domain is null ? null : $"{domain.Spec.Namespace}.{Idioms.TypeName(name, context.Convention)}";
    }

    private static string? DefaultLiteral(ApiField field, string managedType)
    {
        if (field.Default?.Trim() is not { Length: > 0 } v || v is "null" or "none") return null;
        return managedType switch
        {
            "string?" => $"\"{v}\"",
            "bool" when v is "true" or "false" => v,
            "float" when double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) => v + "f",
            "double" when double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) => v,
            "sbyte" or "byte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" when long.TryParse(v, out _) => v,
            var t when t.Contains('.') && !t.EndsWith('?') && !t.StartsWith("System.", StringComparison.Ordinal)
                && System.Text.RegularExpressions.Regex.IsMatch(v, "^-?[A-Za-z0-9_]+$") =>
                long.TryParse(v, out _) ? $"({t}){v}" : $"{t}.{Idioms.EnumMember(v, CTypes.Unqualified(field.Type))}",
            _ => null,
        };
    }

    /// <summary>
    /// The partial class of <paramref name="f"/>: a constructor taking its dependencies and config
    /// fields, deriving from its contract's class when it has one and owning the handle otherwise.
    /// </summary>
    public static string Render(Factory f, Convention convention)
    {
        var o = new StringBuilder();
        o.Append("// <auto-generated/>\n// Derived from the factory header. Do not edit; edit the header instead.\n\n#nullable enable\n\n");
        o.Append($"namespace {f.ManagedNamespace};\n\n");
        o.Append($"/// <summary>{Xml(f.Function.Doc)}</summary>\n");
        o.Append($"public sealed unsafe partial class {f.ClassName}{(f.Contract is null ? " : IDisposable" : $" : {f.ContractClass}")}\n{{\n");
        o.Append($"    /// <summary>{Xml(f.Function.Doc)}</summary>\n");
        foreach (var a in f.Arguments)
            o.Append($"    /// <param name=\"{a.Name}\">{Xml(a.Doc)}</param>\n");
        var handle = HandleTypeName(f);
        var created = CreatedType(f);
        var holds = string.Concat(f.Arguments.Where(a => a.IsRequiredProvider || a.IsOptionalProvider)
            .Select(a => $"        Hold({a.Name}{(a.IsOptionalProvider ? "?" : "")}.Owner);\n"));
        foreach (var output in f.Outputs)
            o.Append($"    /// <summary>{Xml(output.Doc)}</summary>\n    public {output.ManagedType} {output.Property} {{ get; }}\n\n");
        var keep = string.Concat(f.Outputs.Select(output => $" {output.Property} = made.{output.Property};"));
        if (f.Contract is null)
        {
            o.Append($"    public {f.ClassName}({Parameters(f)}) : this(Create({Names(f)}))\n    {{\n{holds}    }}\n\n");
            o.Append($"    private {handle} _handle;\n\n    private readonly List<{convention.OwnerClass}> _held = new();\n\n");
            o.Append(f.Outputs.Count == 0
                ? $"    private {f.ClassName}({handle} handle) => _handle = handle;\n\n"
                : $"    private {f.ClassName}({created} made)\n    {{\n        _handle = made.Handle;{keep}\n    }}\n\n");
            o.Append($"    private void Hold({convention.OwnerClass}? owner)\n    {{\n        if (owner is null) return;\n        var added = false;\n        owner.DangerousAddRef(ref added);\n        _held.Add(owner);\n    }}\n\n");
            o.Append("    /// <summary>Releases the native object, then what it held.</summary>\n    public void Dispose()\n    {\n        if (_handle.@ref == null) return;\n        _handle.destroy(_handle.@ref);\n        _handle = default;\n        foreach (var held in _held) held.DangerousRelease();\n        _held.Clear();\n    }\n\n");
        }
        else if (f.Outputs.Count == 0)
            o.Append($"    public {f.ClassName}({Parameters(f)}) : base(Create({Names(f)}))\n    {{\n{holds}    }}\n\n");
        else
        {
            o.Append($"    public {f.ClassName}({Parameters(f)}) : this(Create({Names(f)}))\n    {{\n{holds}    }}\n\n");
            o.Append($"    private {f.ClassName}({created} made) : base(made.Handle)\n    {{\n       {keep}\n    }}\n\n");
        }
        o.Append($"    private static {created} Create({Parameters(f, withDefaults: false)})\n    {{\n");
        foreach (var a in f.Arguments.Where(a => a.IsRequiredProvider))
            o.Append($"        ArgumentNullException.ThrowIfNull({a.Name});\n");
        foreach (var a in f.Arguments.Where(a => a.IsString))
            o.Append($"        var {a.Name}Bytes = {a.Name} is null ? null : System.Text.Encoding.UTF8.GetBytes({a.Name} + \"\\0\");\n");
        foreach (var a in f.Arguments.Where(a => a.IsString))
            o.Append($"        fixed (byte* {a.Name}Ptr = {a.Name}Bytes)\n");
        var strings = f.Arguments.Any(a => a.IsString);
        var pad = strings ? "            " : "        ";
        if (strings) o.Append("        {\n");
        if (f.Config is not null)
        {
            o.Append($"{pad}{f.Job.Namespace}.{(CSharpWriter.IsValue(f.Config) ? Idioms.TypeName(f.Config.Name, convention) : f.Config.Name)} config = default;\n");
            foreach (var a in f.Arguments.Where(a => a.Field is not null))
                o.Append($"{pad}config.{(CSharpWriter.IsValue(f.Config) ? Idioms.Pascal(a.Field!) : a.Field)} = {(a.IsString ? $"(sbyte*){a.Name}Ptr" : a.Assign)};\n");
        }
        foreach (var group in f.Arguments.Where(a => a.Bundle is not null).GroupBy(a => a.Bundle!.Value))
        {
            o.Append($"{pad}{group.Key.Type} {group.Key.Local} = default;\n");
            foreach (var a in group) o.Append($"{pad}{a.Assign};\n");
        }
        o.Append($"{pad}{handle} handle;\n");
        foreach (var output in f.Outputs)
            o.Append($"{pad}{output.ManagedType} {Idioms.Ident(output.Property)} = default;\n");
        var call = f.Arguments.Where(a => a.Field is null).Select(a => a.Bundle is { } b ? $"&{b.Local}" : a.Assign).Distinct().Concat(f.Config is null ? [] : ["&config"])
            .Concat(f.Outputs.Select(output => $"&{Idioms.Ident(output.Property)}")).Append("&handle");
        o.Append($"{pad}{convention.ResultHelperClass}.ThrowIfFailed({f.Job.Namespace}.{f.Job.MethodsClass}.{convention.StripPrefix(f.Function.Name)}({string.Join(", ", call)}), \"{f.Function.Name}\");\n");
        o.Append(f.Outputs.Count == 0 ? $"{pad}return handle;\n" : $"{pad}return (handle, {string.Join(", ", f.Outputs.Select(output => Idioms.Ident(output.Property)))});\n");
        if (strings) o.Append("        }\n");
        o.Append("    }\n}\n");
        return o.ToString();
    }

    /// <summary>
    /// The constructor's parameter list. A value with a default starts at it, and a nullable one at
    /// null, unless a required argument comes after it; a value with neither is required.
    /// </summary>
    public static string Parameters(Factory f, bool withDefaults = true)
    {
        var lastRequired = f.Arguments.ToList().FindLastIndex(a => a.IsRequired);
        return string.Join(", ", f.Arguments.Select((a, i) =>
            $"{a.ManagedType} {a.Name}{(withDefaults && i > lastRequired ? $" = {a.Default ?? "default"}" : "")}"));
    }

    /// <summary>The native handle type the factory fills.</summary>
    public static string HandleTypeName(Factory f) =>
        f.Contract is null ? $"{f.Job.Namespace}.{f.HandleType}" : $"{f.Contract.Spec.NativeNamespace}.{f.HandleType}";

    /// <summary>What <c>Create</c> returns: the handle, or a tuple of the handle and each output when the factory has any.</summary>
    public static string CreatedType(Factory f) =>
        f.Outputs.Count == 0 ? HandleTypeName(f) : $"({HandleTypeName(f)} Handle, {string.Join(", ", f.Outputs.Select(o => $"{o.ManagedType} {o.Property}"))})";

    /// <summary>Whether the public constructor chains to the private one of the class rather than to its contract's class.</summary>
    public static bool ChainsThroughThis(Factory f) => f.Contract is null || f.Outputs.Count > 0;

    /// <summary>The constructor's argument names, in order.</summary>
    public static string Names(Factory f) => string.Join(", ", f.Arguments.Select(a => a.Name));

    private static string Xml(string? text) =>
        (text ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\n", " ").Trim();
}
