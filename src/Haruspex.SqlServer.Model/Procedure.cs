using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>A stored procedure: its name and its definition as ScriptDom parsed it.</summary>
public sealed class Procedure
{
    /// <summary>Creates a procedure.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="body">Its definition: parameters, and statements or a CLR method.</param>
    /// <param name="file">The file the definition is in, as in <see cref="SourceLocation.File"/>.</param>
    /// <param name="redefinition">Set when the source also changes the procedure elsewhere.</param>
    public Procedure(ObjectName name, ProcedureStatementBody body, string file, Redefinition? redefinition = null)
    {
        Name = name;
        Body = body ?? throw new ArgumentNullException(nameof(body));
        File = file ?? throw new ArgumentNullException(nameof(file));
        Redefinition = redefinition;
    }

    /// <summary>The procedure's name.</summary>
    public ObjectName Name { get; }

    /// <summary>
    /// The definition: a <c>CREATE PROCEDURE</c>, <c>CREATE OR ALTER PROCEDURE</c> or, when the source only alters
    /// the procedure, its <c>ALTER PROCEDURE</c>. Lines and columns in it are positions in <see cref="File"/>.
    /// </summary>
    public ProcedureStatementBody Body { get; }

    /// <summary>The file the definition is in, relative to the source root, as in <see cref="SourceLocation.File"/>.</summary>
    public string File { get; }

    /// <summary>Where the definition starts.</summary>
    public SourceLocation At => new(File, Body.StartLine, Body.StartColumn);

    /// <summary>
    /// Set when the source also alters or drops the procedure, so <see cref="Body"/> may not be the definition in
    /// effect. Null when the definition is the only one.
    /// </summary>
    public Redefinition? Redefinition { get; }

    /// <summary>The procedure's name.</summary>
    public override string ToString() => Name.ToString();
}

/// <summary>
/// A statement that changes an object after its definition, in a way the model does not apply: <c>ALTER TABLE</c>
/// that changes columns, <c>DROP TABLE</c>, <c>ALTER PROCEDURE</c>, <c>DROP PROCEDURE</c>.
/// </summary>
/// <remarks>
/// A folder of scripts does not say the order they run in, so which definition is in effect is unknown. The model
/// records the doubt rather than guessing an order; readers should not trust the object's definition.
/// </remarks>
/// <param name="Name">The object changed.</param>
/// <param name="Statement">The statement, in capitals: <c>ALTER TABLE</c>, <c>DROP PROCEDURE</c>.</param>
/// <param name="At">Where the statement is.</param>
public sealed record Redefinition(ObjectName Name, string Statement, SourceLocation At)
{
    /// <summary>Why the object's definition cannot be trusted, in a sentence.</summary>
    public string Message =>
        $"{Name} is changed by {Statement} at {At}, and which definition applies depends on the order the scripts run.";
}

/// <summary>
/// An object the model knows only by name and kind: a view, synonym or function, whose definition is not
/// modelled yet. Knowing it exists still matters, because a reference to a name can resolve to it.
/// </summary>
/// <param name="Name">The object's name.</param>
/// <param name="Kind">What it is.</param>
public sealed record NamedObject(ObjectName Name, NamedObjectKind Kind);

/// <summary>What a <see cref="NamedObject"/> is.</summary>
public enum NamedObjectKind
{
    /// <summary>A view.</summary>
    View,

    /// <summary>A synonym.</summary>
    Synonym,

    /// <summary>A function of any kind: scalar, table-valued or CLR.</summary>
    Function,
}
