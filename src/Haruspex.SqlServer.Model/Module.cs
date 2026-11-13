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
}
