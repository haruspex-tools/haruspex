namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A string holding JSON: what a JSON value becomes once a driver returns it as a column, once it passes through a
/// scalar function, or when FOR JSON builds it without its array wrapper; and what a column declared to hold JSON of a schema
/// holds.
/// </summary>
/// <param name="Content">
/// What the text parses to when it was built here or declared, which <c>json_query</c> makes live again; null when unknown.
/// </param>
internal sealed record JsonTextShape(Shape? Content = null) : Shape
{
    /// <summary>
    /// The string the text is as a SQL value, when it is a column's, of the column's own type (a declared column's
    /// <c>varchar(4000)</c>); null for JSON text haruspex builds, which is <c>nvarchar(max)</c>. What an expression over it, such
    /// as <c>LEN</c> or a concatenation, is typed from.
    /// </summary>
    public ScalarShape? Text { get; init; }
}
