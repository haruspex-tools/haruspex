using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>A primary key, unique constraint or unique index as declared: columns whose values no two rows share.</summary>
/// <param name="Name">Its name, or null when SQL Server is left to generate one.</param>
/// <param name="Columns">Its columns' names, in key order.</param>
/// <param name="IsPrimaryKey">Whether it is the primary key.</param>
/// <param name="At">Where it is declared.</param>
/// <param name="DisabledAt">
/// Where a statement may disable its index (<c>ALTER INDEX … DISABLE</c>) or drop it (<c>ALTER TABLE … DROP CONSTRAINT</c>,
/// <c>DROP INDEX</c>), so that rows written meanwhile need not be unique; null when none does. Which runs last depends on the
/// order of the scripts, which the source does not say. A DACPAC that can't say whether its index is enabled (an extract's
/// primary key or unique constraint) gives the key's own place.
/// </param>
public sealed record UniqueKey(string? Name, EquatableArray<string> Columns, bool IsPrimaryKey, SourceLocation At, SourceLocation? DisabledAt = null)
{
    /// <summary>
    /// Whether it is declared <c>IGNORE_DUP_KEY = ON</c>, so that a row whose key another has is dropped without an error rather
    /// than refused. The key still holds of the rows kept (observed:
    /// <c>A_key_that_ignores_duplicates_drops_a_duplicate_row_a_table_valued_parameter_is_sent</c>).
    /// </summary>
    public bool IgnoresDuplicates { get; init; }
}
