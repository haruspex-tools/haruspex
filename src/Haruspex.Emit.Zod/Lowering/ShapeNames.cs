using System.Security.Cryptography;
using System.Text;

namespace Haruspex.Emit.Zod.Lowering;

/// <summary>
/// The names of the shapes a module declares once for several types and doesn't export: <c>shape</c> and the start of the
/// SHA-256 of the shape's schema as the module writes it (<c>shape3f2a9c</c>), so that a name depends on its shape alone, not on
/// which types share it or which procedure came first
/// (<c>docs/decisions/0071-a-shared-shape-is-named-by-declaration-or-by-its-structure.md</c>).
/// </summary>
/// <remarks>
/// Six hexadecimal digits, as short as reads well, which two different shapes of one module share rarely but not never. When they
/// do, or when the name is one the module already binds (an imported schema's), the later shape takes more digits until its name
/// is free: two different shapes never get one name, and only a shape that meets such a collision has a longer one. Two schemas
/// of the same text are one shape, so they are never given two names.
/// </remarks>
internal sealed class ShapeNames(IEnumerable<string> taken, int digits = ShapeNames.Digits)
{
    /// <summary>What every such name starts with.</summary>
    public const string Prefix = "shape";

    /// <summary>The hexadecimal digits of the hash a name has when nothing else has taken them.</summary>
    public const int Digits = 6;

    private readonly HashSet<string> bound = new(taken, StringComparer.Ordinal);
    private readonly Dictionary<string, string> byText = new(StringComparer.Ordinal);

    /// <summary>The name of the shape whose schema the module writes as <paramref name="text"/>.</summary>
    public string Of(string text)
    {
        if (byText.TryGetValue(text, out var known))
            return known;
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        for (var length = digits; length <= hash.Length; length++)
        {
            var name = Prefix + hash[..length];
            if (bound.Add(name))
                return byText[text] = name;
        }

        throw new InvalidOperationException($"Every name of the shape {hash} is taken.");
    }
}
