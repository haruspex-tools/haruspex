using Haruspex.Diagnostics;

namespace Haruspex.Emit.Mssql;

/// <summary>
/// The codes of what the mssql emitter refuses (HAR52xx), each explained on its page in <c>docs/diagnostics</c>: a document whose
/// functions it can't name, or a query file whose parameters it can't declare, refused before anything is written.
/// </summary>
public static class MssqlCodes
{
    /// <summary>A procedure whose function's name is reserved, or is a name the module already binds.</summary>
    public static DiagnosticCode FunctionNameTaken { get; } = new(5201, DiagnosticSeverity.Error, "A procedure's function can't take its name");

    /// <summary>
    /// A query file's parameter that its function can't declare to <c>sp_executesql</c>: its type isn't known, or isn't one a
    /// declaration can name, or its name is one of <c>sp_executesql</c>'s own.
    /// </summary>
    public static DiagnosticCode QueryParameterCannotBeDeclared { get; } = new(5202, DiagnosticSeverity.Error, "A query file's parameter can't be declared");

    /// <summary>
    /// A result set named <c>__proto__</c>, which as a key of the object the function returns would set its prototype rather than
    /// add a key.
    /// </summary>
    public static DiagnosticCode SetNameUnusable { get; } = new(5203, DiagnosticSeverity.Error, "A result set's name can't be a key of the function's object");

    /// <summary>
    /// A table type's column whose type gives no driver type to declare it as (a number of no known format), which a table-valued
    /// parameter must declare for every column.
    /// </summary>
    public static DiagnosticCode TableColumnCannotBeDeclared { get; } = new(5204, DiagnosticSeverity.Error, "A table type's column can't be declared to the driver");

    /// <summary>
    /// A parameter named <c>@__proto__</c>, which the driver never sends: it keeps a request's parameters as keys of an object,
    /// where that key sets the object's prototype rather than adding a key.
    /// </summary>
    public static DiagnosticCode ParameterUnusable { get; } = new(5205, DiagnosticSeverity.Error, "A parameter the driver can't send");

    /// <summary>
    /// An <c>OUTPUT</c> parameter of type <c>xml</c>, which the driver can't receive: bound as <c>Xml</c> or as text, the call fails
    /// with error 257 (observed).
    /// </summary>
    public static DiagnosticCode XmlOutputCannotBeReceived { get; } = new(5206, DiagnosticSeverity.Error, "An xml OUTPUT parameter the driver can't receive");
}
