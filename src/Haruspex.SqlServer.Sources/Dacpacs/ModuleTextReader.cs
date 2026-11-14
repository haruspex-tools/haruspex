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
/// <b>An encrypted module.</b> A package built from a project keeps an encrypted module's text; one extracted from a database
/// can't, and keeps a placeholder body and no header. A procedure with no header is read with its parameters and an
/// <see cref="EncryptedBody"/>, so its results are unknown rather than the nothing its placeholder would claim. A function or
/// view with no header can't be read at all (its return type or columns are in the header) and is left out, said once.
/// </para>
/// </remarks>
internal static class ModuleTextReader
{
    /// <summary>A module read from its element; null when it has no text to read, or its text doesn't parse (each problem added to <paramref name="diagnostics"/>).</summary>
    /// <param name="element">The module's element.</param>
    /// <param name="file">Its location's file (<see cref="PackageLocation"/>).</param>
    /// <param name="diagnostics">Where problems are added.</param>
    public static Module? Read(XElement element, string file, ICollection<Diagnostic> diagnostics)
    {
        var type = ModelXml.TypeOf(element);
        var textElement = type is ModelXml.SqlScalarFunction or ModelXml.SqlInlineTableValuedFunction or ModelXml.SqlMultiStatementTableValuedFunction
            ? ModelXml.Elements(element, ModelXml.FunctionBody).FirstOrDefault() ?? element
            : element;
        var header = ModelXml.Annotation(textElement, ModelXml.SysCommentsObjectAnnotation) is { } comments ? ModelXml.Property(comments, ModelXml.HeaderContents) : null;
        var body = ModelXml.Property(textElement, ModelXml.BodyScript) ?? ModelXml.Property(element, ModelXml.QueryScript);
        if (header is null || body is null)
            return WithoutText(element, type, file, diagnostics);

        var quoted = ModelXml.Property(element, ModelXml.IsQuotedIdentifierOn) != "False";
        using var reader = new StringReader(header + body);
        var parsed = new TSql170Parser(quoted).Parse(reader, out var errors);
        foreach (var error in errors)
            diagnostics.Add(new Diagnostic(SourceCodes.ParseError, error.Message, new SourceLocation(file, error.Line, error.Column)));
        if (errors.Count > 0 || parsed is not TSqlScript script || script.Batches.SelectMany(b => b.Statements).FirstOrDefault() is not { } statement)
            return null;

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
    /// A module whose text the package doesn't hold: an encrypted procedure, with its parameters from their elements; anything
    /// else is left out and said (see the remarks).
    /// </summary>
    private static Procedure? WithoutText(XElement element, string type, string file, ICollection<Diagnostic> diagnostics)
    {
        var name = ElementName.Object(ModelXml.Named(element));
        if (!ModelXml.IsTrue(element, ModelXml.IsEncrypted))
        {
            diagnostics.Add(SourceCodes.NotModelled.At(new SourceLocation(file, 1, 1), $"{name} has no text in the package, so it is not read."));
            return null;
        }

        if (type != ModelXml.SqlProcedure)
        {
            diagnostics.Add(SourceCodes.EncryptedNotRead.At(new SourceLocation(file, 1, 1), $"{name} is encrypted, and the package holds a placeholder, not its text: it is left out, so what reads it is unknown."));
            return null;
        }

        var span = PackageLocation.Whole(file, 1);
        var parameters = ModelXml.Elements(element, ModelXml.Parameters).Select(p => Parameter(p, span, diagnostics));
        return new Procedure(name, span, [.. parameters], new EncryptedBody()) { AnsiNullsOff = !ModelXml.IsTrue(element, ModelXml.IsAnsiNullsOn) };
    }

    /// <summary>A parameter from its element: its type, whether it is OUTPUT, and its default parsed from its script.</summary>
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

        return new Parameter(ElementName.Last(ModelXml.Named(parameter)), TypeSpecifier.Of(parameter, ModelXml.Type), ModelXml.IsTrue(parameter, ModelXml.IsOutput), @default, within.Start);
    }
}
