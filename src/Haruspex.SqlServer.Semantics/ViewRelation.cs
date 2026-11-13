using Haruspex.Collections;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A view of the project, read from its definition.</summary>
/// <param name="View">The view.</param>
/// <param name="Columns">Its column list, or else its query's columns, its stars expanded as SQL Server does when it creates the view; null when they can't be listed.</param>
public sealed record ViewRelation(View View, EquatableArray<string>? Columns)
    : RelationSymbol(View?.Name.ToString() ?? throw new ArgumentNullException(nameof(View)), Columns);
