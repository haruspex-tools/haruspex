using System.Data;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// A run's argument, with the type the procedure declares for its parameter, so that it is sent as that type, as an
/// <c>EXEC</c> with a literal converts it. Inferred from the value instead, as <c>AddWithValue</c> does, a <c>date</c> or
/// <c>datetime2</c> argument travels as <c>datetime</c>: rounded to a three-hundredth of a second, and refused before 1753
/// (observed with Microsoft.Data.SqlClient 7.1.1, audit finding F077).
/// </summary>
/// <param name="Value">The value as the CLR type SqlClient sends as <paramref name="Type"/>; null for NULL.</param>
/// <param name="Type">The declared type's SqlClient type.</param>
/// <param name="Length">A string's or binary's declared length, -1 for <c>max</c>; 0 for a type that has none.</param>
/// <param name="Precision">A <c>decimal</c>'s declared precision; 0 for any other type.</param>
/// <param name="Scale">
/// A <c>decimal</c>'s declared scale; 7 for <c>time</c>, <c>datetime2</c> and <c>datetimeoffset</c>, which the server then rounds
/// to the declared scale as it rounds an <c>EXEC</c>'s literal (SqlClient given the declared scale truncates instead).
/// </param>
public sealed record RunArgument(object? Value, SqlDbType Type, int Length = 0, byte Precision = 0, byte Scale = 0)
{
    /// <summary>A parameter named <paramref name="name"/> holding the argument, of the declared type.</summary>
    public SqlParameter Parameter(string name) => new(name, Type)
    {
        Value = Value ?? DBNull.Value,
        Size = Length,
        Precision = Precision,
        Scale = Scale,
    };
}
