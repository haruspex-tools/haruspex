using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>The kinds of <see cref="BindingProblem"/>.</summary>
public enum BindingProblemKind
{
    /// <summary>A table source the binder does not model yet: another database, a temporary table, a derived table, some joins.</summary>
    UnsupportedSource,

    /// <summary>A name that resolves to no table, to an object that is not a table, or to more than one object.</summary>
    UnresolvedTable,

    /// <summary>The table is changed after its definition in a way the model does not apply (see <see cref="Redefinition"/>).</summary>
    TableRedefined,

    /// <summary>A column reference of a form the binder does not model yet, such as a three-part name.</summary>
    UnsupportedReference,

    /// <summary>A column that no table in scope has, or more than one has.</summary>
    UnresolvedColumn,
}
