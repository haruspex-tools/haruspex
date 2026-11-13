using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>One definition of a view, with the statement that gave it: a view defined twice records its second.</summary>
/// <param name="View">The view it defines.</param>
/// <param name="Statement">Which statement it is: a <c>CREATE</c>, an <c>ALTER</c> or a <c>CREATE OR ALTER VIEW</c>.</param>
internal sealed record ViewDefinition(View View, RedefiningStatement Statement);
