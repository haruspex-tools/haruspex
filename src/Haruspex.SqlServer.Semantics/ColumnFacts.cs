using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// What the schema guarantees about a column's non-null values: its type's facets (length, digits, range) and what
/// its CHECK constraints prove (a value set, bounds, a minimum length). Every fact is something SQL Server enforces;
/// anything not proved is left out, which only leaves the type wider. None says anything about null: a CHECK passes
/// when its condition is UNKNOWN, so a constraint never rules null out (nullability is <see cref="SchemaRules.IsNeverNull(DatabaseModel, Table, Column)"/>'s).
/// </summary>
/// <param name="Type">The column's type with any alias resolved, or null when it cannot be resolved.</param>
/// <param name="Length">A string's length, from its type.</param>
/// <param name="MinLength">A string's minimum length, in UTF-16 code units, from a CHECK constraint.</param>
/// <param name="Digits">An exact number's precision and scale, from its type.</param>
/// <param name="Min">A number's lower bound: the tighter of its type's and its CHECK constraints'.</param>
/// <param name="Max">A number's upper bound, likewise.</param>
/// <param name="Values">A set the value is drawn from, from CHECK constraints.</param>
public sealed record ColumnFacts(
    ResolvedType? Type,
    LengthFact? Length = null,
    MinLengthFact? MinLength = null,
    DigitsFact? Digits = null,
    BoundFact? Min = null,
    BoundFact? Max = null,
    ValueSetFact? Values = null);

/// <summary>A type with any alias resolved to the built-in type it stands for.</summary>
/// <param name="BuiltIn">The built-in type, such as <c>nvarchar(20)</c>.</param>
/// <param name="Alias">The alias type it was declared with (<c>sysname</c> included), or null.</param>
/// <param name="AliasNullability">What the alias type says about null; <see cref="DeclaredNullability.NotDeclared"/> without one.</param>
public sealed record ResolvedType(SqlType BuiltIn, string? Alias, DeclaredNullability AliasNullability)
{
    /// <summary>The name facts from the type are attributed to: the alias's, or the built-in type as written.</summary>
    public string SourceName => Alias ?? BuiltIn.ToString();
}

/// <summary>Where a fact came from.</summary>
public abstract record FactSource;

/// <summary>A fact implied by a type, such as <c>tinyint</c>'s range.</summary>
/// <param name="Type">The type's name: an alias's, or the built-in type as written.</param>
public sealed record TypeSource(string Type) : FactSource;

/// <summary>A fact proved by a CHECK constraint.</summary>
public sealed record CheckSource(Table Table, CheckConstraint Check) : FactSource;

/// <summary>What a string length counts.</summary>
public enum TextUnit
{
    /// <summary>UTF-16 code units: <c>nchar(n)</c> and <c>nvarchar(n)</c>.</summary>
    Utf16,

    /// <summary>Bytes of the collation's encoding: <c>char(n)</c> and <c>varchar(n)</c>.</summary>
    Bytes,
}

/// <summary>The encoding of <c>char</c> and <c>varchar</c> data, from the collation.</summary>
public enum TextEncoding
{
    /// <summary>Not known: no collation is declared, and a folder of scripts does not record the database's default.</summary>
    Unknown,

    /// <summary>A single-byte code page: each character one byte, and one UTF-16 code unit.</summary>
    SingleByte,

    /// <summary>UTF-8 (a <c>_UTF8</c> collation): one to four bytes per character.</summary>
    Utf8,

    /// <summary>A double-byte code page (932, 936, 949, 950): one or two bytes per character.</summary>
    DoubleByte,
}

/// <param name="Value">The maximum, or the fixed length.</param>
/// <param name="IsFixed">Whether values are padded to exactly <paramref name="Value"/> (<c>char</c>, <c>nchar</c>).</param>
/// <param name="Unit">What <paramref name="Value"/> counts.</param>
/// <param name="Encoding">For bytes, the encoding they are in.</param>
/// <param name="From">The type.</param>
public sealed record LengthFact(int Value, bool IsFixed, TextUnit Unit, TextEncoding Encoding, FactSource From);

/// <param name="Value">The minimum length in UTF-16 code units.</param>
/// <param name="From">The constraint.</param>
public sealed record MinLengthFact(int Value, FactSource From);

/// <param name="Precision">Digits in all.</param>
/// <param name="Scale">Digits after the point.</param>
/// <param name="From">The type.</param>
public sealed record DigitsFact(int Precision, int Scale, FactSource From);

/// <param name="Value">The bound.</param>
/// <param name="Inclusive">Whether the bound itself is allowed.</param>
/// <param name="From">The type or constraint it came from.</param>
public sealed record BoundFact(ExactDecimal Value, bool Inclusive, FactSource From);

/// <summary>How a value compares with the items of its set.</summary>
public enum ValueComparison
{
    /// <summary>Numerically: every value is one of the numbers.</summary>
    Exact,

    /// <summary>
    /// Under the column's collation, with trailing spaces ignored, as SQL Server compares strings: every value
    /// equals one of the strings, but may differ from it in case, accents or trailing spaces.
    /// </summary>
    Collation,
}

/// <summary>A set the value is drawn from: numbers or strings.</summary>
/// <param name="Numbers">The numbers, sorted, when the column is numeric.</param>
/// <param name="Strings">The strings, in the order the constraint lists them, when the column is a string.</param>
/// <param name="Comparison">How a value compares with them.</param>
/// <param name="From">The constraints that narrowed the set.</param>
public sealed record ValueSetFact(
    IReadOnlyList<ExactDecimal>? Numbers,
    IReadOnlyList<string>? Strings,
    ValueComparison Comparison,
    IReadOnlyList<FactSource> From);
