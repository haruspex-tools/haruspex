using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// An object defined by T-SQL of its own: a procedure, a function, a view or a trigger, the objects SQL Server keeps a
/// definition for (<c>sys.sql_modules</c>). Its definition is kept as ScriptDom parsed it, for binding and inference to read.
/// </summary>
/// <remarks>
/// A synonym is not a module: it has no definition, only the name of what it stands for, so it is a
/// <see cref="SchemaObject"/> beside the modules (decision 0031).
/// </remarks>
#pragma warning disable CA1716 // SQL Server's own word for it. Visual Basic, where it is a keyword, writes it in brackets.
public abstract record Module : SchemaObject
#pragma warning restore CA1716
{
    /// <summary>Creates a module; only the kinds in this assembly can.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="span">Where its definition is.</param>
    private protected Module(ObjectName name, SourceSpan span)
        : base(name, span)
    {
    }

    /// <summary>
    /// Whether it is declared <c>WITH EXECUTE AS</c> anyone but its caller (<c>OWNER</c>, <c>SELF</c> or a user), or its body runs
    /// an <c>EXECUTE AS</c> statement of anyone but its caller (a login or a user): its body, or the part after the statement,
    /// and whatever that calls, then runs with that user's permissions, not the callers'. Only <c>UNMASK</c> among them changes a
    /// claim (<see cref="SessionOptions.Unmask"/>), so this is all that is kept of the clause. False for a view, which has none.
    /// </summary>
    public bool RunsAsAnother { get; init; }
}
