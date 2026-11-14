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

    /// <summary>
    /// The built-in type with its defaults written out, as every value of it is spelt in output: <c>decimal(18,0)</c> for a
    /// <c>decimal</c> declared bare, as for one computed. One database read from its scripts or from its DACPAC then gives one
    /// output, since a DACPAC holds the type DacFx resolved and not how it was written
    /// (<c>docs/decisions/0041-a-type-is-spelt-with-its-defaults-written-out.md</c>). A type the catalogue can't read the
    /// arguments of is spelt as written.
    /// </summary>
    public string Spelt => Scalar?.Text ?? Written(BuiltIn);

    /// <summary>The name facts from the type are attributed to: the alias's, or the built-in type (<see cref="Spelt"/>).</summary>
    public string SourceName => Alias ?? Spelt;

#pragma warning disable CA1308 // Type names are written in lower case, as T-SQL usually is; this is not a comparison.
    private static string Written(SqlType type) =>
        type.Arguments.Count == 0 ? type.Name : type.Name + "(" + string.Join(",", type.Arguments.Select(a => a.ToLowerInvariant())) + ")";
#pragma warning restore CA1308
}
