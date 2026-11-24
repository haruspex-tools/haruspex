using System.Xml.Linq;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>
/// Reads a module (a procedure, function, view or DML trigger) from its text in <c>model.xml</c>: the header DacFx keeps in an
/// annotation (<c>CREATE PROCEDURE … AS</c>, with any comment before it) followed by the body (<c>BodyScript</c>, or a view's
/// <c>QueryScript</c>), which together are the module's definition as written. It is parsed by ScriptDom and read by the same
/// <c>ModuleReader</c> a folder's scripts are, so a module means the same from either.
/// </summary>
/// <remarks>
/// <para>
/// <b>The settings a module was created with reach the parse.</b> SQL Server keeps <c>ANSI_NULLS</c> and <c>QUOTED_IDENTIFIER</c>
/// as they were when a module was created, and a package records both: <c>IsAnsiNullsOn</c>, absent when off, and
/// <c>IsQuotedIdentifierOn</c>, written false when off (observed in <c>atelier</c>'s <c>nulls_off</c> and <c>quoted_off</c>). So
/// <c>"kiln"</c> in a module created with <c>QUOTED_IDENTIFIER</c> off is parsed as the string it is there, and <c>= NULL</c> in one
/// created with <c>ANSI_NULLS</c> off is read as it runs (<see cref="Procedure.AnsiNullsOff"/>).
/// </para>
/// <para>
/// <b>A module without text.</b> A package built from a project keeps an encrypted module's text; one extracted from a database
/// can't, and keeps a placeholder body and no header. A CLR module has no text in either. Such a module is read from its element
/// (<see cref="WithoutText"/>), with an <see cref="EncryptedBody"/> or a <see cref="ClrBody"/>, so that what calls it is typed
/// from what the element declares, and what it does is unknown with the true reason rather than the nothing a placeholder would
/// claim.
/// </para>
/// </remarks>
internal static class ModuleTextReader
{
    /// <summary>A module read from its element; null when it has no text to read, or its text doesn't parse (each problem added to <paramref name="diagnostics"/>).</summary>
    /// <param name="element">The module's element.</param>
    /// <param name="file">Its location's file (<see cref="PackageLocation"/>).</param>
    /// <param name="diagnostics">Where problems are added.</param>
    /// <param name="parsed">
    /// The module's text as parsed, whose statements at any depth may weaken a claim about a table
    /// (<see cref="WeakeningStatements"/>); null when it has none, or it doesn't parse.
    /// </param>
    public static Module? Read(XElement element, string file, ICollection<Diagnostic> diagnostics, out TSqlScript? parsed)
    {
        parsed = null;
        var type = ModelXml.TypeOf(element);
        if (Text(element) is not { } text)
            return WithoutText(element, type, file, diagnostics);

        var quoted = ModelXml.Property(element, ModelXml.IsQuotedIdentifierOn) != "False";
        using var reader = new StringReader(text);
        var fragment = new TSql170Parser(quoted).Parse(reader, out var errors);
        foreach (var error in errors)
            diagnostics.Add(new Diagnostic(SourceCodes.ParseError, error.Message, new SourceLocation(file, error.Line, error.Column)));
        if (errors.Count > 0 || fragment is not TSqlScript script || script.Batches.SelectMany(b => b.Statements).FirstOrDefault() is not { } statement)
            return null;
        parsed = script;

        var ansiNullsOff = !ModelXml.IsTrue(element, ModelXml.IsAnsiNullsOn);
        return statement switch
        {
            ProcedureStatementBody procedure => ModuleReader.Procedure(procedure, file) with { AnsiNullsOff = ansiNullsOff },
            FunctionStatementBody function => ModuleReader.Function(function, file) with { AnsiNullsOff = ansiNullsOff },
            ViewStatementBody view => ModuleReader.View(view, file) with { AnsiNullsOff = ansiNullsOff },
            TriggerStatementBody trigger => ModuleReader.Trigger(trigger, file),
            _ => null,
        };
    }

    /// <summary>
    /// A module's own text, its header and its body, which a location in it counts lines and columns in
    /// (<see cref="PackageLocation"/>); null when the package doesn't hold it.
    /// </summary>
    public static string? Text(XElement element)
    {
        var type = ModelXml.TypeOf(element);
        var textElement = type is ModelXml.SqlScalarFunction or ModelXml.SqlInlineTableValuedFunction or ModelXml.SqlMultiStatementTableValuedFunction
            ? ModelXml.Elements(element, ModelXml.FunctionBody).FirstOrDefault() ?? element
            : element;
        var header = ModelXml.Annotation(textElement, ModelXml.SysCommentsObjectAnnotation) is { } comments ? ModelXml.Property(comments, ModelXml.HeaderContents) : null;
        var body = ModelXml.Property(textElement, ModelXml.BodyScript) ?? ModelXml.Property(element, ModelXml.QueryScript);
        return header is null || body is null ? null : header + body;
    }

    /// <summary>
    /// A module whose text the package doesn't hold, read from its element, which keeps what a caller needs: its parameters, a
    /// scalar function's return type, a table function's columns, a view's column names, a trigger's table and events. Its body
    /// is a <see cref="ClrBody"/> for a CLR module (the element names its assembly, class and method), and an
    /// <see cref="EncryptedBody"/> for one created <c>WITH ENCRYPTION</c> in an extract; a view needs no body. Any other is left
    /// out, and counted in the package's note.
    /// </summary>
    private static Module? WithoutText(XElement element, string type, string file, ICollection<Diagnostic> diagnostics)
    {
        var name = ElementName.Object(ModelXml.Named(element));
        var span = PackageLocation.Whole(file, 1);
        var ansiNullsOff = !ModelXml.IsTrue(element, ModelXml.IsAnsiNullsOn);
        var encrypted = ModelXml.IsTrue(element, ModelXml.IsEncrypted);

        // A view needs no body: an encrypted one's columns are its element's, as are a system view's in master.dacpac.
        if (type == ModelXml.SqlView)
            return new View(name, span, [.. ModelXml.Elements(element, ModelXml.Columns).Select(c => ElementName.Last(ModelXml.Named(c)))], null) { AnsiNullsOff = ansiNullsOff, IsEncrypted = encrypted };

        // Anything else neither CLR nor encrypted (master.dacpac's system procedures, which a call names by sp_ anyway) is not
        // read; the package's one note counts them (PackageReader).
        var implementation = ModelXml.Elements(element, ModelXml.FunctionBody).FirstOrDefault() ?? element;
        ModuleBody? body = Clr(implementation) is { } clr ? clr : encrypted ? new EncryptedBody() : null;
        if (body is null)
            return null;

        Collections.EquatableArray<Parameter> parameters = [.. ModelXml.Elements(element, ModelXml.Parameters).Select(p => Parameter(p, span, diagnostics))];

        // EXECUTE AS anyone but the caller: IsCaller false, as a package writes it for OWNER (observed in WideWorldImporters).
        var another = ModelXml.Property(element, ModelXml.IsCaller) == "False";
        return type switch
        {
            ModelXml.SqlProcedure => new Procedure(name, span, parameters, body) { AnsiNullsOff = ansiNullsOff, RunsAsAnother = another },
            ModelXml.SqlScalarFunction => new ScalarFunction(name, span, parameters, TypeSpecifier.Of(element, ModelXml.Type), body)
            {
                AnsiNullsOff = ansiNullsOff,
                ReturnsNullOnNullInput = ModelXml.IsTrue(element, ModelXml.DoReturnNullForNullInput),
                RunsAsAnother = another,
            },
            ModelXml.SqlInlineTableValuedFunction or ModelXml.SqlMultiStatementTableValuedFunction =>
                new TableFunction(name, span, parameters, ReturnTable(element, name, span, inline: type == ModelXml.SqlInlineTableValuedFunction, diagnostics), body) { AnsiNullsOff = ansiNullsOff, RunsAsAnother = another },
            ModelXml.SqlDmlTrigger => Trigger(element, name, span, body) is { } trigger ? trigger with { RunsAsAnother = another } : null,
            _ => null,
        };
    }

    /// <summary>A CLR body, when the element (a procedure, trigger or function's implementation) names an assembly; otherwise null.</summary>
    private static ClrBody? Clr(XElement implementation) =>
        ModelXml.References(implementation, ModelXml.Assembly).FirstOrDefault() is { } assembly
            ? new ClrBody(ElementName.Last(ModelXml.Named(assembly)), ModelXml.Property(implementation, ModelXml.ClassName) ?? "", ModelXml.Property(implementation, ModelXml.MethodName))
            : null;

    /// <summary>
    /// The table a function without text returns, from the columns its element lists: named by its variable when it has one, as
    /// a body's table variable is, or else as the function is. A multi-statement function's columns are declared, NOT NULL
    /// enforced as in any table. An inline function's are the engine's description of its query, whose nullability is not a
    /// guarantee haruspex can check (a caller's session can make an expression NULL that the description says can't be), so
    /// each is read as nullable, which claims less.
    /// </summary>
    private static DeclaredTable ReturnTable(XElement element, ObjectName function, SourceSpan span, bool inline, ICollection<Diagnostic> diagnostics)
    {
        var reader = new TableReader(span.File, extracted: true, diagnostics);
        var columns = ModelXml.Elements(element, ModelXml.Columns)
            .Select(c => reader.Column(c, ElementName.Last(ModelXml.Named(c)), span.Start))
            .Select(c => inline ? c with { Nullability = DeclaredNullability.Null } : c);
        return new DeclaredTable(ModelXml.Property(element, ModelXml.ReturnTableVariable) ?? function.ToString(), span, [.. columns], [], [], []);
    }

    /// <summary>
    /// A DML trigger without text: its table, its events, and whether it runs instead of the statement (<c>SqlTriggerType</c> 3,
    /// observed on an <c>INSTEAD OF</c> trigger, against 2 on an <c>AFTER</c> one).
    /// </summary>
    private static Trigger? Trigger(XElement element, ObjectName name, SourceSpan span, ModuleBody body)
    {
        if (ModelXml.References(element, ModelXml.Parent).FirstOrDefault() is not { } parent)
            return null;
        var events = (ModelXml.IsTrue(element, ModelXml.IsInsertTrigger) ? DmlEvents.Insert : DmlEvents.None)
            | (ModelXml.IsTrue(element, ModelXml.IsUpdateTrigger) ? DmlEvents.Update : DmlEvents.None)
            | (ModelXml.IsTrue(element, ModelXml.IsDeleteTrigger) ? DmlEvents.Delete : DmlEvents.None);
        return new Trigger(name, span, ElementName.Object(ModelXml.Named(parent)), ModelXml.Property(element, ModelXml.SqlTriggerType) == "3", events, body);
    }

    /// <summary>A parameter from its element: its type, whether it is OUTPUT or READONLY, and its default parsed from its script.</summary>
    private static Parameter Parameter(XElement parameter, SourceSpan within, ICollection<Diagnostic> diagnostics)
    {
        ScalarExpression? @default = null;
        if (ModelXml.Property(parameter, ModelXml.DefaultExpressionScript) is { } script)
        {
            using var reader = new StringReader(script);
            @default = new TSql170Parser(true).ParseExpression(reader, out var errors);
            foreach (var error in errors)
                diagnostics.Add(new Diagnostic(SourceCodes.ParseError, error.Message, within.Start));
        }

        return new Parameter(ElementName.Last(ModelXml.Named(parameter)), TypeSpecifier.Of(parameter, ModelXml.Type), ModelXml.IsTrue(parameter, ModelXml.IsOutput), @default, within.Start)
        {
            IsReadOnly = ModelXml.IsTrue(parameter, ModelXml.IsReadOnly),
        };
    }
}
