using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A type with any alias resolved to the built-in type it stands for.</summary>
public sealed record ResolvedType
{
    /// <summary>A built-in type as declared, read once into the catalogue's terms.</summary>
    /// <param name="builtIn">The built-in type as written, such as <c>nvarchar(20)</c>.</param>
    /// <param name="alias">The alias type it was declared with (<c>sysname</c> included), or null.</param>
    /// <param name="aliasNullability">What the alias type says about null; <see cref="DeclaredNullability.NotDeclared"/> without one.</param>
    public ResolvedType(SqlType builtIn, string? alias, DeclaredNullability aliasNullability)
        : this(builtIn, ScalarType.Of(builtIn), alias, aliasNullability)
    {
    }

    private ResolvedType(SqlType builtIn, ScalarType? scalar, string? alias, DeclaredNullability aliasNullability)
    {
        BuiltIn = builtIn ?? throw new ArgumentNullException(nameof(builtIn));
        Scalar = scalar;
        Alias = alias;
        AliasNullability = aliasNullability;
    }

    /// <summary>A type the server's rules computed, which no declaration names.</summary>
    public static ResolvedType Of(ScalarType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new ResolvedType(type.Declaration, type, null, DeclaredNullability.NotDeclared);
    }

    /// <summary>The built-in type as written, such as <c>nvarchar(20)</c> or <c>decimal</c>.</summary>
    public SqlType BuiltIn { get; }

    /// <summary>The catalogue's entry for it, or null for a name the catalogue doesn't hold.</summary>
    public BuiltInType? Type => Scalar?.Type ?? BuiltInType.Of(BuiltIn);

    /// <summary>
    /// It with its arguments, the defaults applied (<c>decimal</c> is <c>decimal(18,0)</c>); null when its arguments can't be
    /// read.
    /// </summary>
    public ScalarType? Scalar { get; }

    /// <summary>The alias type it was declared with (<c>sysname</c> included), or null.</summary>
    public string? Alias { get; }

    /// <summary>What the alias type says about null; <see cref="DeclaredNullability.NotDeclared"/> without one.</summary>
    public DeclaredNullability AliasNullability { get; }

    /// <summary>The name facts from the type are attributed to: the alias's, or the built-in type as written.</summary>
    public string SourceName => Alias ?? BuiltIn.ToString();
}
