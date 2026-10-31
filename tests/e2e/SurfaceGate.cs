using System.Reflection;

/// <summary>Fails naming every public member of a namespace that exposes a raw pointer.</summary>
internal static class SurfaceGate
{
    const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static void Check(Assembly assembly, string ns)
    {
        var raw = assembly.GetExportedTypes()
            .Where(t => t.Namespace == ns && !t.Name.StartsWith("INative", StringComparison.Ordinal))
            .SelectMany(t => t.GetMembers(Declared).Select(m => (Type: t, Member: m)))
            .Where(x => x.Member.Name != "Borrow" && Signature(x.Member).Any(IsRaw))
            .Select(x => $"{x.Type.Name}.{x.Member}")
            .ToList();
        foreach (var member in raw.Take(20)) Console.WriteLine(member);
        if (raw.Count > 0) throw new InvalidOperationException($"{ns}: {raw.Count} raw public members");
    }

    static IEnumerable<Type> Signature(MemberInfo member) => member switch
    {
        MethodBase m => m.GetParameters().Select(p => p.ParameterType).Append(m is MethodInfo i ? i.ReturnType : typeof(void)),
        PropertyInfo p => [p.PropertyType],
        FieldInfo f => [f.FieldType],
        _ => [],
    };

    static bool IsRaw(Type type) => type.IsPointer || type.IsFunctionPointer || (type.HasElementType && IsRaw(type.GetElementType()!));
}
