using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A table's full-text index, by the unique index it names as its key: <c>CREATE FULLTEXT INDEX ON t (…) KEY INDEX ix</c>. Each
/// row's value in that index's one column is the row's key in the full-text index, which <c>CONTAINSTABLE</c> and
/// <c>FREETEXTTABLE</c> return as <c>KEY</c>.
/// </summary>
/// <param name="Index">The name of the key index as written: a unique index, unique constraint or primary key of the table.</param>
/// <param name="At">Where the full-text index is declared.</param>
public sealed record FullTextKey(string Index, SourceLocation At);
