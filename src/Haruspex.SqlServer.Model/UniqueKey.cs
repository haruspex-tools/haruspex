using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>A primary key, unique constraint or unique index as declared: columns whose values no two rows share.</summary>
/// <param name="Name">Its name, or null when SQL Server is left to generate one.</param>
/// <param name="Columns">Its columns' names, in key order.</param>
/// <param name="IsPrimaryKey">Whether it is the primary key.</param>
/// <param name="At">Where it is declared.</param>
/// <param name="DisabledAt">
/// Where a statement may disable its index (<c>ALTER INDEX … DISABLE</c>), so that rows written meanwhile need not be
/// unique; null when none does. Which runs last depends on the order of the scripts, which the source does not say.
/// </param>
public sealed record UniqueKey(string? Name, IReadOnlyList<string> Columns, bool IsPrimaryKey, SourceLocation At, SourceLocation? DisabledAt = null);
