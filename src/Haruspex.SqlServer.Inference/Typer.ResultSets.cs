using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// The result sets EXEC … WITH RESULT SETS declares, typed from the declaration alone. README.md in this folder ("Result sets a
// call declares") gives the rules, and ExecObservationTests.ResultSets.cs in the differential tests the observations.
internal sealed partial class Typer
{
    /// <summary>
    /// The result set one definition of <c>WITH RESULT SETS</c> declares: a rowset of the declared columns, present whenever the
    /// call succeeds. Each value arrives converted to its column's type as <c>CAST</c> converts (a string cut short, an int too
    /// long for a string written <c>*</c>, anything that doesn't convert an error), so a column claims its type's facts and
    /// nothing of the callee's value. Only <c>NOT NULL</c> written in the declaration makes a column non-null: without either, a
    /// column follows the caller's <c>ANSI_NULL_DFLT</c>, and an alias type's <c>NOT NULL</c> is not applied.
    /// </summary>
    /// <param name="definition">The set's definition: its columns, a table or table type that gives them, or <c>AS FOR XML</c>.</param>
    /// <param name="position">Its position among the declared sets, from 0, which a reason names.</param>
    internal ResultShape DeclaredSet(ResultSetDefinition definition, int position)
    {
        var at = At(definition);
        var row = definition switch
        {
            InlineResultSetDefinition inline => new Row([.. inline.ResultColumnDefinitions.Select(c => DeclaredColumn(c, position))], null),
            SchemaObjectResultSetDefinition named => NamedColumns(named, position),
            _ => new Row([], Unsupported(
                IrUnknownReason.UnsupportedQuery,
                $"Result set {position + 1} is declared AS FOR XML: one ntext column named as FOR XML output is, whose rows the mssql driver joins, which is not modelled.",
                at)),
        };
        return new ResultShape(IrEncoding.Rowset, Rowset(row), []);
    }

    /// <summary>A column a definition lists: its name, and its type as a value converted to it.</summary>
    private RowColumn DeclaredColumn(ResultColumnDefinition column, int position)
    {
        var name = column.ColumnDefinition.ColumnIdentifier.Value;
        var at = At(column);
        var notNull = column.Nullable is { Nullable: false };
        return new RowColumn(name, DeclaredValue(SyntaxReader.Type(column.ColumnDefinition.DataType, search.Own), notNull, name, position, at), at);
    }

    /// <summary>
    /// The columns of <c>AS OBJECT</c> or <c>AS TYPE</c>: a table's or a table type's, each its type as a value converted to it,
    /// never null where the server's metadata says so (<see cref="SchemaRules.IsNeverNull"/>). Its CHECK constraints are not
    /// enforced on what arrives, so they give no facet. A view's and a function's columns are as the server describes them,
    /// which may claim less or more than inference proves of them, and a computed or hidden column's nullability is the
    /// server's too, so neither is modelled; nor is a column set, nor a name without its schema, which the module's schema may
    /// resolve.
    /// </summary>
    private Row NamedColumns(SchemaObjectResultSetDefinition definition, int position)
    {
        var at = At(definition);
        Row Fails(string detail) => new([], Unsupported(IrUnknownReason.UnsupportedQuery, $"Result set {position + 1} is declared {Text(definition)}: {detail}", at));

        if (Binder.Qualified(definition.Name) is not { } name)
            return Fails("a name without its schema, or in another database, is not modelled.");
        ITable? table = definition.ResultSetType == ResultSetType.Object
            ? model.FindTable(name)
            : model.FindUserType(name) as TableType;
        if (table is null)
        {
            return Fails(definition.ResultSetType == ResultSetType.Object
                ? "a view's or a function's columns, or those of an object the project doesn't define, are not modelled."
                : "a type the project doesn't define as a table type is not modelled.");
        }

        // A column set stands for its sparse columns in some reads and not others, which is not observed here.
        if (table.Columns.FirstOrDefault(c => c.IsComputed || c.IsHidden || c.IsColumnSet) is { } computed)
            return Fails($"its column {computed.Name} is computed, hidden or a column set, whose place and nullability are the server's, which is not modelled.");
        return new Row([.. table.Columns.Select(c => new RowColumn(
            c.Name,
            DeclaredValue(c.Type, SchemaRules.IsNeverNull(model, table, c), c.Name, position, c.At),
            at))], null);
    }

    /// <summary>A value converted to <paramref name="type"/>, as a declared column holds it.</summary>
    private Value DeclaredValue(SqlType? type, bool notNull, string column, int position, SourceLocation at)
    {
        var shape = SqlTypes.ToConvertedShape(model, type, at);
        if (shape is UnknownShape unknown)
            return Value.Unknown(unknown);
        if (type is { Collection: not null } || SchemaRules.ResolveType(model, type)?.Type == BuiltInType.Json)
            return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedSqlType, $"{column}, declared of the type {type}, is not modelled.", at));
        EquatableArray<IrNullReason> because = notNull ? [] : [new IrNullReason(IrNullRule.NullableColumn, $"{column} of result set {position + 1} declared at {at}")];
        return Value.Computed(shape, because);
    }
}
