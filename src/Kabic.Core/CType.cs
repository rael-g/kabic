using System.Text.RegularExpressions;

namespace Kabic;

/// <summary>
/// A C type read once from the text clang gives: its base name, whether what it reaches is const,
/// its levels of pointer and its array extents, outermost first.
/// </summary>
public sealed record CType(string Name, bool Const, int Pointers, IReadOnlyList<int> Extents)
{
    static readonly Regex Extent = new(@"\[(\d*)\]");

    /// <summary>Whether the type is a pointer.</summary>
    public bool IsPointer => Pointers > 0;

    /// <summary>Whether the type is an array.</summary>
    public bool IsArray => Extents.Count > 0;

    /// <summary>Reads <paramref name="text"/>, the way clang spells a type.</summary>
    public static CType Of(string text)
    {
        var extents = Extent.Matches(text).Select(m => m.Groups[1].Length == 0 ? 0 : int.Parse(m.Groups[1].Value)).ToList();
        var body = Extent.Replace(text, " ");
        var pointers = body.Count(c => c == '*');
        var words = body.Replace("*", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("struct" or "enum" or "union" or "const" or "volatile" or "restrict")).ToList();
        var leading = body.Split('*')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new CType(string.Join(' ', words), leading.Contains("const"), pointers, extents);
    }
}
