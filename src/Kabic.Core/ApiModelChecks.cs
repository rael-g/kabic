namespace Kabic;

/// <summary>
/// The rules a description has to keep whatever language it is projected to. Each one is checked
/// once, when the model is read, so no writer restates it.
/// </summary>
public static class ApiModelChecks
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> naming the first declaration whose
    /// <c>[array_of:]</c> or <c>[closure:]</c> names a sibling that does not exist.
    /// </summary>
    public static void Validate(ApiModel model)
    {
        foreach (var s in model.Structs)
        {
            foreach (var f in s.Fields)
                if (f.CountedBy is { } count && s.Fields.All(x => x.Name != count))
                    throw new InvalidOperationException(
                        $"{s.Name}.{f.Name}: [array_of:{count}] names a length field the struct does not declare");
            foreach (var slot in s.Slots) Validate(slot);
        }
        foreach (var fn in model.Functions)
            Validate(new ApiSlot(fn.Name, fn.Returns, [], fn.Doc, fn.ReturnDoc, fn.Params.ToList()) { ReturnTags = fn.ReturnTags });
    }

    static void Validate(ApiSlot slot)
    {
        bool Declares(string name) => slot.Params.Any(p => p.Name == name);
        foreach (var p in slot.Params)
        {
            if (p.CountedBy is { } count && !Declares(count))
                throw new InvalidOperationException(
                    $"{slot.Name}.{p.Name}: [array_of:{count}] names a length parameter the slot does not declare");
            if (p.Has("closure") && !p.Has("completion"))
            {
                var ctx = p.ClosureContext
                    ?? throw new InvalidOperationException(
                        $"{slot.Name}.{p.Name}: [closure] must name the context parameter, as [closure:<name>]");
                if (!Declares(ctx))
                    throw new InvalidOperationException(
                        $"{slot.Name}.{p.Name}: [closure:{ctx}] names no parameter of this slot");
            }
        }
        if (slot.ReturnCountedBy is { } returned && !Declares(returned))
            throw new InvalidOperationException(
                $"{slot.Name}: the return's [array_of:{returned}] names a length parameter the slot does not declare");
    }
}
