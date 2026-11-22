using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The codes of what inference reports (HAR2xxx), each explained on its page in <c>docs/diagnostics</c>: an unknown in the
/// output, a note with one code for each of its reasons but those only a declaration resolves (<see cref="Of"/>), and what
/// inference could not check, a warning. The notes first given to the reasons only a declaration resolves (2002, 2016 to
/// 2020) are retired: the config layer reports those unknowns, each once, with what to declare.
/// </summary>
public static class InferenceCodes
{
    /// <summary>A write to a table the project doesn't define, so its triggers, which could return results, can't be seen.</summary>
    public static DiagnosticCode UndefinedWriteTarget { get; } = new(2101, DiagnosticSeverity.Warning, "A write to an object the project doesn't define");

    /// <summary>
    /// A JSON path that never finds a value in JSON whose shape is known, since the shape can never have what it asks for: a key
    /// a closed object doesn't have, a key of a string or an array, an index past a fixed array's end.
    /// </summary>
    public static DiagnosticCode PathFindsNothing { get; } = new(2102, DiagnosticSeverity.Warning, "A JSON path that never finds a value");

    /// <summary>An unknown for <see cref="IrUnknownReason.UnsupportedStatement"/>.</summary>
    public static DiagnosticCode UnsupportedStatement { get; } = Unknown(2001, "A statement that isn't modelled yet");

    /// <summary>An unknown for <see cref="IrUnknownReason.Redefined"/>.</summary>
    public static DiagnosticCode Redefined { get; } = Unknown(2003, "An object whose definition depends on the order of scripts");

    /// <summary>An unknown for <see cref="IrUnknownReason.UnsupportedQuery"/>.</summary>
    public static DiagnosticCode UnsupportedQuery { get; } = Unknown(2004, "A query form that isn't modelled yet");

    /// <summary>An unknown for <see cref="IrUnknownReason.UnsupportedExpression"/>.</summary>
    public static DiagnosticCode UnsupportedExpression { get; } = Unknown(2005, "An expression that isn't modelled yet");

    /// <summary>An unknown for <see cref="IrUnknownReason.UnsupportedSource"/>.</summary>
    public static DiagnosticCode UnsupportedSource { get; } = Unknown(2006, "A table source that isn't modelled yet");

    /// <summary>An unknown for <see cref="IrUnknownReason.UnsupportedSqlType"/>.</summary>
    public static DiagnosticCode UnsupportedSqlType { get; } = Unknown(2007, "A SQL type that isn't modelled yet");

    /// <summary>An unknown for <see cref="IrUnknownReason.UnresolvedTable"/>.</summary>
    public static DiagnosticCode UnresolvedTable { get; } = Unknown(2008, "A table the project doesn't define");

    /// <summary>An unknown for <see cref="IrUnknownReason.UnresolvedColumn"/>.</summary>
    public static DiagnosticCode UnresolvedColumn { get; } = Unknown(2009, "A column that can't be found or is ambiguous");

    /// <summary>An unknown for <see cref="IrUnknownReason.UnnamedColumn"/>.</summary>
    public static DiagnosticCode UnnamedColumn { get; } = Unknown(2010, "A result column without a name");

    /// <summary>An unknown for <see cref="IrUnknownReason.DuplicateColumn"/>.</summary>
    public static DiagnosticCode DuplicateColumn { get; } = Unknown(2011, "Two result columns with the same key");

    /// <summary>An unknown for <see cref="IrUnknownReason.UnresolvedRecursion"/>.</summary>
    public static DiagnosticCode UnresolvedRecursion { get; } = Unknown(2012, "Recursion that reached no fixed point");

    /// <summary>An unknown for <see cref="IrUnknownReason.JsonShapeUnknown"/>.</summary>
    public static DiagnosticCode JsonShapeUnknown { get; } = Unknown(2013, "JSON whose shape isn't visible");

    /// <summary>An unknown for <see cref="IrUnknownReason.CallerTempTable"/>.</summary>
    public static DiagnosticCode CallerTempTable { get; } = Unknown(2014, "A temporary table the caller may create");

    /// <summary>An unknown for <see cref="IrUnknownReason.VaryingResults"/>.</summary>
    public static DiagnosticCode VaryingResults { get; } = Unknown(2015, "Result sets that depend on the path taken");

    /// <summary>An unknown for <see cref="IrUnknownReason.TriggerResults"/>.</summary>
    public static DiagnosticCode TriggerResults { get; } = Unknown(2021, "A trigger that may return result sets");

    /// <summary>An unknown for <see cref="IrUnknownReason.HiddenColumn"/>.</summary>
    public static DiagnosticCode HiddenColumn { get; } = Unknown(2022, "A result set with a hidden column");

    /// <summary>An unknown for <see cref="IrUnknownReason.SqlcmdVariable"/>.</summary>
    public static DiagnosticCode SqlcmdVariable { get; } = Unknown(2023, "A SQLCMD variable a deployment replaces");

    /// <summary>Retired: an unknown for <see cref="IrUnknownReason.ExternalCode"/> is reported as HAR4204 or HAR4205.</summary>
    public static DiagnosticCode ExternalCode { get; } = Retire(2002, "Results from code outside the project");

    /// <summary>Retired: an unknown for <see cref="IrUnknownReason.DynamicSql"/> is reported as HAR4204 or HAR4205.</summary>
    public static DiagnosticCode DynamicSql { get; } = Retire(2016, "Dynamic SQL");

    /// <summary>Retired: an unknown for <see cref="IrUnknownReason.ProcedureVariable"/> is reported as HAR4204 or HAR4205.</summary>
    public static DiagnosticCode ProcedureVariable { get; } = Retire(2017, "A procedure chosen when the call runs");

    /// <summary>Retired: an unknown for <see cref="IrUnknownReason.SystemProcedure"/> is reported as HAR4204 or HAR4205.</summary>
    public static DiagnosticCode SystemProcedure { get; } = Retire(2018, "A system procedure");

    /// <summary>Retired: an unknown for <see cref="IrUnknownReason.MissingProcedure"/> is reported as HAR4204 or HAR4205.</summary>
    public static DiagnosticCode MissingProcedure { get; } = Retire(2019, "A procedure that isn't in the project");

    /// <summary>Retired: an unknown for <see cref="IrUnknownReason.AmbiguousProcedure"/> is reported as HAR4204 or HAR4205.</summary>
    public static DiagnosticCode AmbiguousProcedure { get; } = Retire(2020, "A procedure name more than one schema has");

    /// <summary>A query file's parameter whose type its uses don't decide, which a declaration in the file decides instead.</summary>
    public static DiagnosticCode UndecidedParameter { get; } = Unknown(2024, "A query file's parameter its uses don't decide");

    /// <summary>
    /// JSON built as text, <c>'[' + string_agg(…) + ']'</c> and its kin, found by its construction wherever it is written
    /// (<see cref="JsonTextNotes"/>): the place, and the constructor that builds the same JSON, escaped by the engine and typed
    /// exactly. An inventory for modernisation, not an unknown: such text is typed where the server guarantees its shape.
    /// </summary>
    public static DiagnosticCode JsonBuiltAsText { get; } = new(2025, DiagnosticSeverity.Note, "JSON built as text");

    /// <summary>
    /// The code of the note an unknown with <paramref name="reason"/> is reported with; null for a reason only a declaration
    /// can resolve (code outside the project, an encrypted definition, dynamic SQL, a procedure chosen when it runs, a system procedure, one outside
    /// the project or one more than one schema has), whose unknowns the config layer reports instead, each once, with what to
    /// declare (HAR4204, HAR4205). A switch over every reason, so a reason added to the IR fails the build here until it is
    /// given a code or said to be one of those.
    /// </summary>
    public static DiagnosticCode? Of(IrUnknownReason reason) => reason switch
    {
        IrUnknownReason.UnsupportedStatement => UnsupportedStatement,
        IrUnknownReason.ExternalCode => null,
        IrUnknownReason.Redefined => Redefined,
        IrUnknownReason.UnsupportedQuery => UnsupportedQuery,
        IrUnknownReason.UnsupportedExpression => UnsupportedExpression,
        IrUnknownReason.UnsupportedSource => UnsupportedSource,
        IrUnknownReason.UnsupportedSqlType => UnsupportedSqlType,
        IrUnknownReason.UnresolvedTable => UnresolvedTable,
        IrUnknownReason.UnresolvedColumn => UnresolvedColumn,
        IrUnknownReason.UnnamedColumn => UnnamedColumn,
        IrUnknownReason.DuplicateColumn => DuplicateColumn,
        IrUnknownReason.UnresolvedRecursion => UnresolvedRecursion,
        IrUnknownReason.JsonShapeUnknown => JsonShapeUnknown,
        IrUnknownReason.CallerTempTable => CallerTempTable,
        IrUnknownReason.VaryingResults => VaryingResults,
        IrUnknownReason.DynamicSql => null,
        IrUnknownReason.ProcedureVariable => null,
        IrUnknownReason.SystemProcedure => null,
        IrUnknownReason.MissingProcedure => null,
        IrUnknownReason.AmbiguousProcedure => null,
        IrUnknownReason.TriggerResults => TriggerResults,
        IrUnknownReason.HiddenColumn => HiddenColumn,
        IrUnknownReason.EncryptedDefinition => null,
        IrUnknownReason.SqlcmdVariable => SqlcmdVariable,
        IrUnknownReason.UndecidedParameter => UndecidedParameter,
    };

    private static DiagnosticCode Unknown(int number, string title) => new(number, DiagnosticSeverity.Note, title);

    private static DiagnosticCode Retire(int number, string title) => Unknown(number, title) with { Retired = true };
}
