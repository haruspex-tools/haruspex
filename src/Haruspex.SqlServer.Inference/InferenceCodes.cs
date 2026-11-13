using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The codes of what inference reports (HAR2xxx), each explained on its page in <c>docs/diagnostics</c>: an unknown in the
/// output, a note with one code for each of its reasons, and what inference could not check, a warning.
/// </summary>
public static class InferenceCodes
{
    /// <summary>A write to a table the project doesn't define, so its triggers, which could return results, can't be seen.</summary>
    public static DiagnosticCode UndefinedWriteTarget { get; } = new(2101, DiagnosticSeverity.Warning, "A write to an object the project doesn't define");

    /// <summary>An unknown for <see cref="IrUnknownReason.UnsupportedStatement"/>.</summary>
    public static DiagnosticCode UnsupportedStatement { get; } = Unknown(2001, "A statement that isn't modelled yet");

    /// <summary>An unknown for <see cref="IrUnknownReason.ExternalCode"/>.</summary>
    public static DiagnosticCode ExternalCode { get; } = Unknown(2002, "Results from code outside the project");

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

    /// <summary>An unknown for <see cref="IrUnknownReason.DynamicSql"/>.</summary>
    public static DiagnosticCode DynamicSql { get; } = Unknown(2016, "Dynamic SQL");

    /// <summary>An unknown for <see cref="IrUnknownReason.ProcedureVariable"/>.</summary>
    public static DiagnosticCode ProcedureVariable { get; } = Unknown(2017, "A procedure chosen when the call runs");

    /// <summary>An unknown for <see cref="IrUnknownReason.SystemProcedure"/>.</summary>
    public static DiagnosticCode SystemProcedure { get; } = Unknown(2018, "A system procedure");

    /// <summary>An unknown for <see cref="IrUnknownReason.MissingProcedure"/>.</summary>
    public static DiagnosticCode MissingProcedure { get; } = Unknown(2019, "A procedure that isn't in the project");

    /// <summary>An unknown for <see cref="IrUnknownReason.AmbiguousProcedure"/>.</summary>
    public static DiagnosticCode AmbiguousProcedure { get; } = Unknown(2020, "A procedure name more than one schema has");

    /// <summary>An unknown for <see cref="IrUnknownReason.TriggerResults"/>.</summary>
    public static DiagnosticCode TriggerResults { get; } = Unknown(2021, "A trigger that may return result sets");

    /// <summary>An unknown for <see cref="IrUnknownReason.HiddenColumn"/>.</summary>
    public static DiagnosticCode HiddenColumn { get; } = Unknown(2022, "A result set with a hidden column");

    /// <summary>
    /// The code of an unknown with <paramref name="reason"/>. A switch over every reason, so a reason added to the IR fails the
    /// build here until it has a code.
    /// </summary>
    public static DiagnosticCode Of(IrUnknownReason reason) => reason switch
    {
        IrUnknownReason.UnsupportedStatement => UnsupportedStatement,
        IrUnknownReason.ExternalCode => ExternalCode,
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
        IrUnknownReason.DynamicSql => DynamicSql,
        IrUnknownReason.ProcedureVariable => ProcedureVariable,
        IrUnknownReason.SystemProcedure => SystemProcedure,
        IrUnknownReason.MissingProcedure => MissingProcedure,
        IrUnknownReason.AmbiguousProcedure => AmbiguousProcedure,
        IrUnknownReason.TriggerResults => TriggerResults,
        IrUnknownReason.HiddenColumn => HiddenColumn,
    };

    private static DiagnosticCode Unknown(int number, string title) => new(number, DiagnosticSeverity.Note, title);
}
