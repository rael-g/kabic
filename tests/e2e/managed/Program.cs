using Probe;

IProbe probe = new ManagedProbe();
probe.Fill([new ProbeItem { Id = new ProbeId(7), Weight = 2.5f }]);
if (probe.Read(0) != 2.5f || probe.Batch().Items.Length != 1) throw new InvalidOperationException("managed probe");
Console.WriteLine($"managed {probe.Count()}");

sealed class ManagedProbe : IProbe
{
    ProbeItem[] items = [];

    public uint Count() => (uint)items.Length;
    public float WeightOf(uint id) => items[id].Weight;
    public void Fill(ReadOnlySpan<ProbeItem> given) => items = given.ToArray();
    public float Read(uint id) => id < items.Length ? items[id].Weight : throw new InvalidOperationException("read");
    public void Each(ProbeVisit? visit) { foreach (var item in items) visit?.Invoke(item.Id.Value); }
    public ProbeBatch Batch() => new(items);
    public void Tag(ProbeLabel label) { }
    public Task<uint> Load(uint id) => Task.FromResult(id + 1);
    public uint CountOf(ProbeItem? item) => item?.Id.Value ?? (uint)items.Length;
    public void Bind(uint id, object instance) { }
    public void Unbind(uint id) { }
    public void Dispose() { }
}
