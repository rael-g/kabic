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
            {
                if (f.CountedBy is { } count && s.Fields.All(x => x.Name != count))
                    throw new InvalidOperationException(
                        $"{s.Name}.{f.Name}: [array_of:{count}] names a length field the struct does not declare");
                if (f.CountedBy is not null && !CTypes.IsPointer(f.Type) && CTypes.FixedArray(f.Type) is null)
                    throw new InvalidOperationException(
                        $"{s.Name}.{f.Name}: [array_of] bounds the elements a pointer or an array reaches, and {f.Type.Trim()} is neither");
                if (s.Has("borrowed") && CTypes.IsPointer(f.Type) && f.CountedBy is null && !f.Has("utf8"))
                    throw new InvalidOperationException(
                        $"{s.Name}.{f.Name}: a pointer in a [borrowed] struct is memory the call reads, and"
                        + " one that names no count has no extent to read -- tag it [array_of:<count field>].");
            }
            foreach (var slot in s.Slots) Validate(model, slot);
            if (s.Has("bag"))
                Validate(model, new ApiSlot(s.Name, "", [], null, null,
                    s.Fields.Select(f => new ApiParam(f.Name, f.Type, f.Tags, f.Doc)).ToList()));
        }
        foreach (var fn in model.Functions)
            Validate(model, new ApiSlot(fn.Name, fn.Returns, [], fn.Doc, fn.ReturnDoc, fn.Params.ToList()) { ReturnTags = fn.ReturnTags });
    }

    static void Validate(ApiModel model, ApiSlot slot)
    {
        bool Declares(string name) => slot.Params.Any(p => p.Name == name);
        foreach (var p in slot.Params)
        {
            if (p.CountedBy is { } count && !Declares(count))
                throw new InvalidOperationException(
                    $"{slot.Name}.{p.Name}: [array_of:{count}] names a length parameter the slot does not declare");
            foreach (var form in new[] { "completion", "closure" })
            {
                if (!p.Has(form) || (form == "closure" && p.Has("completion"))) continue;
                var ctx = p.TagValue(form)
                    ?? throw new InvalidOperationException(
                        $"{slot.Name}.{p.Name}: [{form}] must name the context parameter it travels with, as [{form}:<name>]");
                if (!Declares(ctx))
                    throw new InvalidOperationException(
                        $"{slot.Name}.{p.Name}: [{form}:{ctx}] names no parameter of this slot");
                var callback = model.CallbackOf(p)
                    ?? throw new InvalidOperationException(
                        $"{slot.Name}.{p.Name}: [{form}] needs a function-pointer typedef, but {p.Type.Trim()} is not one");
                var lane = callback.Lanes.FirstOrDefault(l => l.Has("context"))
                    ?? throw new InvalidOperationException(
                        $"{slot.Name}.{p.Name}: [{form}:{ctx}] hands the context to {callback.Name}, which declares"
                        + " no lane to receive it -- the typedef has to mark it [context]");
                if (lane.Type.Replace(" ", "") != "void*")
                    throw new InvalidOperationException(
                        $"{slot.Name}.{p.Name}: {callback.Name}'s [context] lane is {lane.Type.Trim()}, and a context travels as void*");
            }
            if (p.TagValue("retained") is { } key)
            {
                if (key == "return" ? slot.Returns.Trim() is "void" : !Declares(key))
                    throw new InvalidOperationException(key == "return"
                        ? $"{slot.Name}.{p.Name}: [retained:return] keys the registration on what the slot returns, and this one returns nothing"
                        : $"{slot.Name}.{p.Name}: [retained:{key}] names no parameter of this slot");
            }
            if (p.Has("teardown") && !p.Has("retained"))
                throw new InvalidOperationException(
                    $"{slot.Name}.{p.Name}: [teardown] says the native side calls this handler while the provider"
                    + " is being destroyed, which only a [retained] closure survives to see");
        }
        if (slot.Has("releases"))
        {
            var name = slot.TagValue("releases")
                ?? throw new InvalidOperationException(
                    $"{slot.Name}: [releases] must name the projection that owns what this slot takes back, as [releases:<name>]");
            var released = slot.Params.Count(p => !model.Structs.Any(v => v.IsVtable && v.Name == CTypes.Unqualified(CTypes.Deref(p.Type)).Trim()));
            if (released != 1)
                throw new InvalidOperationException(
                    $"{slot.Name}: [releases:{name}] releases the one pointer this slot takes back,"
                    + $" and it takes {released} parameter(s) besides the provider itself");
        }
        if (slot.ReturnCountedBy is { } returned && !Declares(returned))
            throw new InvalidOperationException(
                $"{slot.Name}: the return's [array_of:{returned}] names a length parameter the slot does not declare");
    }
}
