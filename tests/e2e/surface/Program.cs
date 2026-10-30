using System.Reflection;

const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
var unsafeMembers = new[] { typeof(Probe.Probe).Assembly, typeof(Other.Other).Assembly }
    .SelectMany(a => a.GetExportedTypes())
    .Where(t => t.Namespace is "Probe" or "Other" && !t.Name.StartsWith("INative", StringComparison.Ordinal))
    .SelectMany(t => t.GetMembers(Declared).Select(m => (Type: t, Member: m)))
    .Where(x => x.Member.Name != "Borrow" && Signature(x.Member).Any(IsRaw))
    .Select(x => $"{x.Type.Name}.{x.Member}")
    .ToList();
foreach (var member in unsafeMembers) Console.WriteLine(member);
return unsafeMembers.Count == 0 ? 0 : 1;

static IEnumerable<Type> Signature(MemberInfo member) => member switch
{
    MethodBase m => m.GetParameters().Select(p => p.ParameterType).Append(m is MethodInfo i ? i.ReturnType : typeof(void)),
    PropertyInfo p => [p.PropertyType],
    FieldInfo f => [f.FieldType],
    _ => [],
};

static bool IsRaw(Type type) => type.IsPointer || type.IsFunctionPointer || (type.HasElementType && IsRaw(type.GetElementType()!));
