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
if (unsafeMembers.Count > 0) return 1;

using var probe = new Probe.Probe(4);
var item = Probe.Native.ProbeItem.Default;
item.Id = 7;
probe.Fill([item, item with { Id = 9, Weight = 2.5f }]);
Check(probe.Count() == 2, "count");
Check(probe.Read(1) == 2.5f, "read");
Check(probe.WeightOf(0) == 1f, "default");
var seen = new List<uint>();
probe.Each(seen.Add);
Check(seen.SequenceEqual([7u, 9u]), "closure");
Check(probe.Load(41).GetAwaiter().GetResult() == 42, "completion");
Check(probe.Batch().Items.Length == 2, "view");
try { probe.Read(5); Check(false, "failure"); } catch (InvalidOperationException) { }
var bound = new object();
probe.Bind(1, bound);
probe.Unbind(1);
return 0;

static void Check(bool passed, string what)
{
    if (!passed) throw new InvalidOperationException(what);
}

static IEnumerable<Type> Signature(MemberInfo member) => member switch
{
    MethodBase m => m.GetParameters().Select(p => p.ParameterType).Append(m is MethodInfo i ? i.ReturnType : typeof(void)),
    PropertyInfo p => [p.PropertyType],
    FieldInfo f => [f.FieldType],
    _ => [],
};

static bool IsRaw(Type type) => type.IsPointer || type.IsFunctionPointer || (type.HasElementType && IsRaw(type.GetElementType()!));
