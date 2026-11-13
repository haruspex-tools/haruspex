namespace Haruspex.SqlServer.Semantics;

/// <summary>One column a <c>SELECT *</c> stands for (<see cref="Binder.Star"/>).</summary>
/// <param name="Name">The column's name.</param>
/// <param name="Column">The column, resolved: a <see cref="TableColumn"/> or a <see cref="RelationColumn"/>.</param>
public sealed record StarColumn(string Name, Resolution Column);
