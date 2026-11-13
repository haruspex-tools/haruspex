using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Haruspex.Analysers;

/// <summary>
/// What a closed hierarchy is, as haruspex writes one (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md): an
/// abstract class (a record, usually) of haruspex whose every constructor is <c>private</c>, <c>private protected</c> or
/// <c>internal</c>, so that only its own assembly can derive from it. Its cases are the types that do and are not abstract
/// themselves.
/// </summary>
internal static class ClosedHierarchies
{
    /// <summary>Whether <paramref name="type"/> is the root of a closed hierarchy.</summary>
    public static bool IsRoot(ITypeSymbol? type) => type is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: true } root && IsClosed(root);

    /// <summary>
    /// Whether code in another assembly can name the type: it is public, and so is every type it is nested in. A case of a
    /// closed hierarchy such code can't name is one its switches can't see, so it would take them to be complete.
    /// </summary>
    public static bool IsVisibleOutside(ITypeSymbol type)
    {
        for (var t = type; t is not null; t = t.ContainingType)
        {
            if (t.DeclaredAccessibility != Accessibility.Public)
                return false;
        }

        return true;
    }

    /// <summary>The cases of <paramref name="type"/> when it is a closed hierarchy; empty when it is not one.</summary>
    public static ImmutableArray<INamedTypeSymbol> Cases(ITypeSymbol? type)
    {
        if (!IsRoot(type))
            return [];
        var root = (INamedTypeSymbol)type!;

        var cases = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        foreach (var candidate in Types(root.ContainingAssembly.GlobalNamespace))
        {
            if (!candidate.IsAbstract && Derives(candidate, root))
                cases.Add(candidate);
        }

        return cases.ToImmutable();
    }

    /// <summary>
    /// Whether the type is one of haruspex's own and no other assembly can call a constructor of it: each is private, private
    /// protected or internal. Read from another assembly, such constructors are not there at all (the compiler imports only
    /// what that assembly could call), which says the same. The copy constructor the compiler writes for a record is
    /// protected, but a derived record calls it only from its own, which must chain to one of the others; it is recognised by
    /// its shape, one parameter of the record's own type, since read from another assembly it is not marked as written by the
    /// compiler. A library's abstract type is never taken to be closed: its derived types that this compilation can't see
    /// would be cases no switch could name.
    /// </summary>
    private static bool IsClosed(INamedTypeSymbol type) =>
        type.ContainingAssembly.Name.StartsWith("Haruspex.", StringComparison.Ordinal)
        && type.InstanceConstructors
            .Where(c => !(type.IsRecord && c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, type)))
            .All(c => c.DeclaredAccessibility is Accessibility.Private or Accessibility.ProtectedAndInternal or Accessibility.Internal);

    /// <summary>Whether <paramref name="type"/> derives from <paramref name="root"/>, at any depth.</summary>
    public static bool Derives(ITypeSymbol type, ITypeSymbol root)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, root))
                return true;
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol space)
    {
        foreach (var member in space.GetMembers())
        {
            if (member is INamespaceSymbol inner)
            {
                foreach (var type in Types(inner))
                    yield return type;
            }
            else if (member is INamedTypeSymbol type)
            {
                foreach (var nested in Nested(type))
                    yield return nested;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> Nested(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var inner in type.GetTypeMembers())
        {
            foreach (var nested in Nested(inner))
                yield return nested;
        }
    }
}
