namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// What a variable reference (<c>@name</c>) means, as <see cref="Binder.Resolve(Microsoft.SqlServer.TransactSql.ScriptDom.VariableReference, NameEnvironment)"/>
/// finds it: a parameter of the body, a variable it declares, or neither.
/// </summary>
/// <remarks>
/// A closed hierarchy: <see cref="ParameterVariable"/>, <see cref="LocalVariable"/>, <see cref="UndeclaredVariable"/>,
/// <see cref="CaseVariantVariable"/>.
/// </remarks>
public abstract record VariableResolution
{
    private protected VariableResolution()
    {
    }

    /// <summary>The variable's name, with its <c>@</c>: as declared, or as written when it is undeclared.</summary>
    public abstract LocalName Name { get; }
}
