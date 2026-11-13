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

    /// <summary>
    /// The cases of <paramref name="type"/> when it is a closed hierarchy; empty when it is not one, or when, for a generic
    /// hierarchy, some type derived from it can't be matched exactly by a pattern over <paramref name="type"/> (below).
    /// </summary>
    public static ImmutableArray<INamedTypeSymbol> Cases(ITypeSymbol? type)
    {
        if (!IsRoot(type))
            return [];
        var root = (INamedTypeSymbol)type!;

        var cases = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        foreach (var candidate in Types(root.ContainingAssembly.GlobalNamespace))
        {
            if (candidate.IsAbstract)
                continue;
            switch (Fit(candidate, root, out var found))
            {
                case Fitting.Case:
                    cases.Add(found!);
                    break;
                case Fitting.Unsure:
                    return [];
            }
        }

        return cases.ToImmutable();
    }

    /// <summary>How a type derived from a hierarchy's definition stands to a switch over the hierarchy with its arguments.</summary>
    private enum Fitting
    {
        /// <summary>Not a case of it: it derives from the hierarchy with other arguments, which a value of it can never have.</summary>
        None,

        /// <summary>A case, which a pattern names as constructed.</summary>
        Case,

        /// <summary>Maybe a case, in a form no pattern names exactly; the switch is not known to be complete.</summary>
        Unsure,
    }

    /// <summary>
    /// <paramref name="candidate"/> as a case of <paramref name="root"/>. A case of a generic hierarchy is declared with type
    /// parameters of its own (<c>Done&lt;T&gt; : Ending&lt;T&gt;</c>), and a switch is over the hierarchy with arguments
    /// (<c>Ending&lt;int&gt;</c>), whose cases are the declared ones with the same arguments (<c>Done&lt;int&gt;</c>), which is
    /// what a pattern names. That holds only when the case passes the root its own parameters, each once, and has no others.
    /// Any other shape, a parameter the root doesn't fix (<c>Extra&lt;T, U&gt; : Ending&lt;T&gt;</c>), an argument built from one
    /// (<c>Done&lt;T&gt; : Ending&lt;List&lt;T&gt;&gt;</c>), or a case closed over one argument (<c>IntOnly : Ending&lt;int&gt;</c>)
    /// in a switch whose arguments are still parameters, could be a case that no list of constructed types holds, so the
    /// hierarchy is not taken as closed for that switch: the compiler's own check stands, and the switch needs its discard arm.
    /// A case closed over arguments other than a switch's concrete ones is not a case of it.
    /// </summary>
    private static Fitting Fit(INamedTypeSymbol candidate, INamedTypeSymbol root, out INamedTypeSymbol? found)
    {
        found = null;
        if (!root.IsGenericType)
        {
            found = candidate;
            return Derives(candidate, root) ? Fitting.Case : Fitting.None;
        }

        for (var t = candidate.BaseType; t is not null; t = t.BaseType)
        {
            if (!SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, root.OriginalDefinition))
                continue;

            if (candidate.TypeParameters.IsEmpty)
            {
                found = candidate;
                if (SymbolEqualityComparer.Default.Equals(t, root))
                    return Fitting.Case;
                return Mentions(root) ? Fitting.Unsure : Fitting.None;
            }

            var arguments = new ITypeSymbol?[candidate.TypeParameters.Length];
            for (var i = 0; i < t.TypeArguments.Length; i++)
            {
                if (t.TypeArguments[i] is not ITypeParameterSymbol parameter
                    || !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, candidate)
                    || arguments[parameter.Ordinal] is not null)
                {
                    return Fitting.Unsure;
                }

                arguments[parameter.Ordinal] = root.TypeArguments[i];
            }

            if (arguments.Any(a => a is null))
                return Fitting.Unsure;
            found = candidate.Construct([.. arguments.OfType<ITypeSymbol>()]);
            return Fitting.Case;
        }

        return Fitting.None;
    }

    /// <summary>Whether a type parameter appears anywhere in <paramref name="type"/>'s arguments.</summary>
    private static bool Mentions(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        INamedTypeSymbol named => named.TypeArguments.Any(Mentions),
        IArrayTypeSymbol array => Mentions(array.ElementType),
        _ => false,
    };

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
