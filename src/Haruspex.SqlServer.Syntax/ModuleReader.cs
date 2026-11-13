using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Syntax;

/// <summary>
/// Reads the definitions ScriptDom parsed into the model's modules and synonyms: a procedure, a function of each kind, a
/// view, a trigger. What each keeps of the syntax is its body and its expressions; its name, parameters, return type and
/// options become model values, so that no reader of the model needs ScriptDom to know what an object is.
/// </summary>
public static class ModuleReader
{
    /// <summary>A procedure, from its <c>CREATE</c>, <c>CREATE OR ALTER</c> or <c>ALTER PROCEDURE</c>.</summary>
    /// <param name="definition">The definition.</param>
    /// <param name="file">The file it is in, relative to the source root.</param>
    public static Procedure Procedure(ProcedureStatementBody definition, string file)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var span = SyntaxReader.Span(file, definition);
        return new Procedure(
            SyntaxReader.Name(definition.ProcedureReference.Name),
            span,
            Parameters(definition.Parameters, span),
            Body(definition.StatementList, definition.MethodSpecifier));
    }

    /// <summary>
    /// A function, from its <c>CREATE</c>, <c>CREATE OR ALTER</c> or <c>ALTER FUNCTION</c>: scalar, inline or
    /// multi-statement, by what it returns.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="file">The file it is in, relative to the source root.</param>
    public static Function Function(FunctionStatementBody definition, string file)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var span = SyntaxReader.Span(file, definition);
        var name = SyntaxReader.Name(definition.Name);
        var parameters = Parameters(definition.Parameters, span);
        return definition.ReturnType switch
        {
            ScalarFunctionReturnType scalar => new ScalarFunction(name, span, parameters, SyntaxReader.Type(scalar.DataType), Body(definition.StatementList, definition.MethodSpecifier))
            {
                ReturnsNullOnNullInput = definition.Options?.Any(o => o.OptionKind == FunctionOptionKind.ReturnsNullOnNullInput) == true,
            },
            SelectFunctionReturnType inline => new InlineFunction(name, span, parameters, inline.SelectStatement),
            TableValuedFunctionReturnType table => new TableFunction(name, span, parameters, ReturnTable(name, table.DeclareTableVariableBody, span), Body(definition.StatementList, definition.MethodSpecifier)),

            // ScriptDom's three kinds of return are all T-SQL has.
            _ => throw new ArgumentException($"{name} returns {definition.ReturnType?.GetType().Name ?? "nothing"}, which is not a kind of function.", nameof(definition)),
        };
    }

    /// <summary>A view, from its <c>CREATE</c>, <c>CREATE OR ALTER</c> or <c>ALTER VIEW</c>.</summary>
    /// <param name="definition">The definition.</param>
    /// <param name="file">The file it is in, relative to the source root.</param>
    public static View View(ViewStatementBody definition, string file)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new View(
            SyntaxReader.Name(definition.SchemaObjectName),
            SyntaxReader.Span(file, definition),
            [.. definition.Columns.Select(c => c.Value)],
            definition.SelectStatement);
    }

    /// <summary>
    /// A DML trigger, from its <c>CREATE</c>, <c>CREATE OR ALTER</c> or <c>ALTER TRIGGER</c>; null for a DDL or logon trigger,
    /// which no DML fires.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="file">The file it is in, relative to the source root.</param>
    public static Trigger? Trigger(TriggerStatementBody definition, string file)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.TriggerObject is not { TriggerScope: TriggerScope.Normal, Name: { } on })
            return null;
        var events = DmlEvents.None;
        foreach (var action in definition.TriggerActions)
        {
            events |= action.TriggerActionType switch
            {
                TriggerActionType.Insert => DmlEvents.Insert,
                TriggerActionType.Update => DmlEvents.Update,
                TriggerActionType.Delete => DmlEvents.Delete,
                _ => DmlEvents.None,
            };
        }

        var parent = SyntaxReader.Name(on);

        // A trigger is in its table's schema, whatever its own name says.
        var name = new ObjectName(parent.Schema, definition.Name.BaseIdentifier.Value);
        return new Trigger(name, SyntaxReader.Span(file, definition), parent, definition.TriggerType == TriggerType.InsteadOf, events, Body(definition.StatementList, definition.MethodSpecifier));
    }

    /// <summary>A synonym, from its <c>CREATE SYNONYM</c>.</summary>
    /// <param name="definition">The definition.</param>
    /// <param name="file">The file it is in, relative to the source root.</param>
    public static Synonym Synonym(CreateSynonymStatement definition, string file)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var target = definition.ForName;
        return new Synonym(
            SyntaxReader.Name(definition.Name),
            SyntaxReader.Span(file, definition),
            new SynonymTarget(target.ServerIdentifier?.Value, target.DatabaseIdentifier?.Value, target.SchemaIdentifier?.Value, target.BaseIdentifier.Value, FragmentText.OneLine(target)));
    }

    /// <summary>A procedure's or function's parameters, in declaration order.</summary>
    /// <param name="parameters">The parameters as ScriptDom parsed them.</param>
    /// <param name="within">The span of the definition they are in.</param>
    public static EquatableArray<Parameter> Parameters(IEnumerable<ProcedureParameter> parameters, SourceSpan within)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return [.. parameters.Select(p => new Parameter(p.VariableName.Value, SyntaxReader.Type(p.DataType), p.Modifier == ParameterModifier.Output, p.Value, within.At(p)))];
    }

    /// <summary>What a definition runs: its statements, a CLR method, or nothing when it has neither.</summary>
    private static ModuleBody Body(StatementList? statements, MethodSpecifier? method) => (statements, method) switch
    {
        (_, { } clr) => new ClrBody(clr.AssemblyName.Value, clr.ClassName.Value, clr.MethodName?.Value),
        ({ } sql, null) => new SqlBody(sql),
        (null, null) => new NoBody(),
    };

    /// <summary>
    /// The table a multi-statement function returns: its variable's declaration, read as any table variable's is. A CLR
    /// function declares its table with no variable, so its table is named as the function is.
    /// </summary>
    private static DeclaredTable ReturnTable(ObjectName function, DeclareTableVariableBody declaration, SourceSpan within) =>
        SyntaxReader.Declared(declaration.VariableName?.Value ?? function.ToString(), declaration.Definition, within.Within(declaration));
}
