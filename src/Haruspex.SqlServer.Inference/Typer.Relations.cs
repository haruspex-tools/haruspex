using System.Collections.Immutable;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// What the relations and variables a query reads hold: a CTE (a recursive one by its fixed point), a derived table, the
// body's own tables as declared, a variable as the state holds it, and a literal integer. README.md in this folder ("The
// state", "CTEs") gives the rules and the observations they rest on (StatementFlowObservationTests).
internal sealed partial class Typer
{
    internal static EquatableArray<IrNullReason> Because(IrNullRule rule, LocalName name, Shape shape) =>
        shape is UnknownShape unknown ? [new IrNullReason(rule, name.Text), new IrNullReason(IrNullRule.Unknown, unknown.At.ToString())] : [new IrNullReason(rule, name.Text)];

    /// <summary>The type a variable or parameter is declared with, as <see cref="DeclaredShape(SqlType, SourceLocation, LocalName, out bool)"/> gives it; null for one undeclared.</summary>
    internal Shape? DeclaredShape(VariableResolution resolved) => resolved switch
    {
        ParameterVariable parameter => DeclaredShape(parameter.Parameter.Type, parameter.Parameter.At, resolved.Name, out _),
        LocalVariable local => DeclaredShape(local.Type, local.At, local.Name, out _),
        UndeclaredVariable => null,
    };

    /// <summary>What a table variable holds as declared; JSON written to it is in the state (<see cref="Holding"/>).</summary>
    /// <remarks>
    /// <para>
    /// Its keys are its declaration's: one of a table type, a table-valued parameter among them, has the type's, which SQL Server
    /// enforces on every row sent or written (observed: <c>A_table_valued_parameter_s_key_refuses_a_duplicate_row</c>), and one
    /// declared with its own columns has the primary key, unique constraints and unfiltered unique indexes it declares
    /// (<c>A_table_variable_enforces_the_keys_it_declares</c>). So a query that fixes one reads at most one row, as of a table. No
    /// statement can add or remove one.
    /// </para>
    /// <para>
    /// Its columns are the type's, so a value read from one names the type's column, as a table's does (<c>sales.line_list.note</c>),
    /// both in its reasons and as where it came from (<see cref="LocalRelationValues.From"/>), which <c>haruspex explain</c> starts a
    /// derivation at.
    /// </para>
    /// </remarks>
    internal LocalRelationValues TableVariableValues(DeclaredTable definition)
    {
        var named = definition.Type?.Name.ToString() ?? definition.Name;
        return new([.. definition.Columns.Select(c => c.Name)], [.. definition.Columns.Select(c => DeclaredValue(definition, c, named, temporary: false))], null)
        {
            Keys = [.. TableKeys(definition)],
            From = definition.Type is { } type ? [.. type.Columns.Select(c => (EquatableArray<IrSourceColumn>)[TypeColumn(type, c)])] : null,
        };
    }

    /// <summary>A table type's column as a source of a value, named as a table's column is (<c>sales.line_list.note</c>).</summary>
    internal IrSourceColumn TypeColumn(TableType type, Column column) =>
        new($"{type.Name}.{column.Name}", column.IsComputed ? null : (ColumnShape(type, type.Name.ToString(), column) as ScalarShape)?.Type.Sql, column.At.ToString());

    /// <summary>A body's table's enforced keys, by its columns' places, each typed for comparing it with a value in its own type.</summary>
    internal IEnumerable<RelationKey> TableKeys(DeclaredTable table)
    {
        foreach (var key in SchemaRules.EnforcedKeys(table))
        {
            var ordinals = key.Columns.Select(c => table.Columns.ToList().FindIndex(d => d.Name.Equals(c, StringComparison.OrdinalIgnoreCase))).ToList();
            var types = key.Columns.Select(c => SchemaRules.ResolveType(model, table.Column(c)?.Type)?.Type).ToList();
            if (ordinals.All(o => o >= 0) && types.All(t => t is not null))
                yield return new RelationKey([.. ordinals], [.. types.OfType<BuiltInType>()]);
        }
    }

    /// <summary>A variable's or parameter's type: every value it holds has been converted to it, so the type's facets hold.</summary>
    internal Shape DeclaredShape(SqlType? type, SourceLocation at, LocalName name, out bool live)
    {
        // A json value nests as JSON (observed for variables and columns), and its shape is in the data.
        live = SchemaRules.ResolveType(model, type)?.Type == BuiltInType.Json;
        return live
            ? Unsupported(IrUnknownReason.JsonShapeUnknown, $"{name} is a json variable: its shape is in the data.", at)
            : SqlTypes.ToConvertedShape(model, type, at);
    }

    private Value Variable(VariableReference variable, ReadScope scope)
    {
        // A function's parameter holds its argument for this call; a procedure's, and a variable, what the walk says, which it
        // keeps under the declared name (the state is joined, and passed to a callee, by name).
        var resolved = Binder.Resolve(variable, scope.Names);
        if (resolved is ParameterVariable && parameters.TryGetValue(resolved.Name, out var argument))
            return argument;
        var name = resolved.Name;
        if (resolved is UndeclaredVariable || !scope.State.Variables.TryGetValue(name, out var value))
            return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"The variable {name} is not declared.", At(variable)));

        // A cursor's query takes its variables as they were when its DECLARE ran (observed:
        // A_cursors_query_takes_its_variables_as_they_are_when_it_is_declared), not where the FETCH reads it: any value of
        // the variable's declared type, which every assignment converts to, or null.
        if (scope.Fetched && DeclaredShape(resolved) is { } declared)
            return Value.Computed(declared, Because(IrNullRule.Variable, name, declared), value.IsLiveJson);

        // A variable holding JSON built here holds it as text.
        return scope.State.Json.TryGetValue(new VariableSlot(name), out var slot) ? slot.Read(value) : value;
    }

    /// <summary>
    /// The value an assignment writes: the expression, unless a compound one (<c>+=</c>) combines it with the old. A CASE or
    /// COALESCE over JSON assigned is the same text whether or not the server folds it (<see cref="ReadScope.FoldingMoot"/>).
    /// </summary>
    internal Value AssignedValue(AssignmentKind kind, ScalarExpression expression, ReadScope scope, TSqlFragment at) =>
        kind == AssignmentKind.Equals
            ? Expression(expression, scope.FoldingMootAt(Strip(expression)))
            : Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"The compound assignment {Text(at)} is not modelled yet.", At(at)));

    /// <summary>
    /// What a declared column of a temporary table or table variable holds: its type, with the facts its CHECK constraints
    /// prove, and null unless its declaration (NOT NULL, the primary key, IDENTITY) rules it out.
    /// </summary>
    internal Value DeclaredValue(DeclaredTable table, Column column, string name, bool temporary)
    {
        var where = $"{name}.{column.Name}";
        EquatableArray<IrNullReason> because = SchemaRules.IsNeverNull(model, table, column) ? [] : [new IrNullReason(IrNullRule.NullableColumn, where)];

        // A #table is created in tempdb, which does not have the database's alias types (observed: 2715), so a name there
        // does not mean this project's type. sysname is the system's own, which tempdb has. A table variable takes them from
        // the database (observed).
        if (temporary && column.Type is { Kind: SqlTypeKind.Named } named
            && BuiltInType.SystemAlias(named) is null)
        {
            return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedSqlType, $"{where} is of the alias type {named}, which tempdb has only if it was created there.", column.At));
        }

        if (!column.IsComputed && SchemaRules.ResolveType(model, column.Type)?.Type == BuiltInType.Json)
            return new Value(Unsupported(IrUnknownReason.JsonShapeUnknown, $"{where} is a json column: its shape is in the data.", column.At), because, IsLiveJson: true);

        var shape = ColumnShape(table, table.Name, column);
        return shape is UnknownShape unknown ? Value.Unknown(unknown) : Value.Computed(shape, because);
    }

    // ---- CTEs -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs <paramref name="body"/> with the CTEs of <paramref name="with"/> in scope, as binding binds them, each with what it
    /// holds attached in order, so that each may read the ones before it. A CTE is part of its statement's query, so its values
    /// pass through unchanged, live JSON included (observed).
    /// </summary>
    /// <param name="with">The statement's WITH, or null.</param>
    /// <param name="at">The statement's scope.</param>
    /// <param name="body">What reads the statement, in its scope with the CTEs.</param>
    /// <param name="failed">What a WITH that is not modelled gives.</param>
    internal T WithCtes<T>(WithCtesAndXmlNamespaces? with, ReadScope at, Func<ReadScope, T> body, Func<UnknownShape, T> failed)
    {
        if (with is null)
            return body(at);
        if (with.XmlNamespaces is not null)
            return failed(Unsupported(IrUnknownReason.UnsupportedQuery, "WITH XMLNAMESPACES is not modelled yet.", At(with)));

        var bound = binder.BindWith(with, at.Names);
        var scope = at.In(QueryScope.Empty(null, bound.Names));
        foreach (var cte in bound.Ctes)
            scope = scope.Naming(cte.Symbol, Cte(cte, scope));
        return body(scope);
    }

    /// <param name="bound">The CTE, bound.</param>
    /// <param name="scope">The statement's scope, with the CTEs before it attached.</param>
    private LocalRelationValues Cte(BoundCommonTable bound, ReadScope scope)
    {
        var symbol = bound.Symbol;
        var name = symbol.Name;
        var at = At(symbol.Definition);
        LocalRelationValues Fails(IrUnknownReason reason, string detail) => LocalRelationValues.Unknown(Unsupported(reason, $"{name}: {detail}", at));

        if (!symbol.IsRecursive)
        {
            var row = Query(bound.Query, scope);
            return Holds(symbol, row) ?? Unlisted(name, row, at);
        }

        // A recursive CTE is an anchor member, then recursive members, each joined by UNION ALL; SQL Server refuses any
        // other form. Several anchors (themselves a UNION) are not modelled yet.
        var members = new List<BoundQuery>();
        var operators = new List<BinaryQueryExpression>();
        Flatten(bound.Query, members, operators);
        var recursive = members.Select(m => m.Reads(symbol)).ToList();
        if (recursive[0] || recursive.Skip(1).Contains(false) || members.Any(m => m is not BoundSpecification)
            || operators.Any(o => o.BinaryQueryExpressionType != BinaryQueryExpressionType.Union || !o.All))
        {
            return Fails(IrUnknownReason.UnsupportedQuery, "only a recursive CTE of one anchor member and recursive members, joined by UNION ALL, is modelled.");
        }

        // The column types are the anchor's exactly (SQL Server refuses a recursive member of any other, error 240,
        // observed), so only nullability, the facets the members share and whether a column is a constant need a fixed
        // point: the recursive members are read with the CTE as it stands, joined in, and read again until the values,
        // compared whole, don't change. Each pass only joins upwards. A recursive member's value is never a constant
        // (observed: A_recursive_ctes_column_is_not_a_constant_even_when_every_member_is_one_literal), and the join drops
        // the anchor's digits (Value.Digits), so even a member that gives the anchor's literal again takes one more pass.
        // The members are bound once (Binder.BindWith); each pass attaches the CTE's values as they stand.
        var anchorRow = Query((BoundSpecification)members[0], scope);
        if (Holds(symbol, anchorRow) is not { Names: { } columns, Values: { } anchor } current)
            return Unlisted(name, anchorRow, at);
        LocalRelationValues? failed = null;
        var iteration = FixedPoint.Iterate(
            anchor,
            values =>
            {
                var pass = scope.Naming(symbol, current with { Values = values });
                var next = values.ToList();
                foreach (var member in members.Skip(1).Cast<BoundSpecification>())
                {
                    var row = Query(member, pass);
                    if (row.Unlisted is not null || row.Columns.Count != next.Count)
                    {
                        failed = Fails(IrUnknownReason.UnsupportedQuery, "a recursive member's columns cannot be listed against the anchor's.");
                        return Pass<EquatableArray<Value>>.Stop();
                    }

                    for (var i = 0; i < next.Count; i++)
                        next[i] = Value.JoinConverted(next[i], row.Columns[i].Value, row.Columns[i].At, $"{name}.{columns[i]}");
                }

                return Pass<EquatableArray<Value>>.To([.. next]);
            });

        return iteration.End switch
        {
            IterationEnd.Settled => current with { Values = iteration.Last },
            IterationEnd.Stopped => failed!, // Never null: the step stops only after setting it.
            _ => Fails(IrUnknownReason.UnresolvedRecursion, $"its columns reached no fixed point in {FixedPoint.MostPasses} passes."),
        };
    }

    /// <summary>
    /// What a relation over a query holds: the query's values under the names binding gave the relation (its column list, or
    /// its query's); null when binding could not list them.
    /// </summary>
    private static LocalRelationValues? Holds(RelationSymbol relation, Row row) => Holds(relation.Columns, row);

    /// <summary>What a relation over a query holds, under the names binding listed for it; null when it listed none.</summary>
    private static LocalRelationValues? Holds(EquatableArray<string>? names, Row row)
    {
        if (names is null)
            return null;
        if (row.Unlisted is not null || row.Columns.Count != names.Count)
            throw new InvalidOperationException($"Binding listed {names.Count} columns where the query reads {row.Columns.Count}.");
        return new LocalRelationValues([.. names], [.. row.Columns.Select(c => c.Value)], null) { From = [.. row.Columns.Select(c => c.From)] };
    }

    /// <summary>A chain of set operations' queries in order, and the operators between them; parentheses group a query of their own.</summary>
    private static void Flatten(BoundQuery query, List<BoundQuery> members, List<BinaryQueryExpression> operators)
    {
        if (query is BoundSetOperation operation)
        {
            Flatten(operation.First, members, operators);
            operators.Add(operation.Operation);
            Flatten(operation.Second, members, operators);
        }
        else
        {
            members.Add(query);
        }
    }

    // ---- reading the body's own relations and expressions it needs ----------------------------------------------------

    /// <summary>
    /// A column of a relation (a CTE, a derived table, a temporary table, a table variable, a view and the rest): what it
    /// holds, never null where a filter proves it (Typer.Refinement.cs), and null too on an outer join's optional side unless a
    /// filter proves the row matched.
    /// </summary>
    /// <param name="source">The source it is read through.</param>
    /// <param name="ordinal">Its position among the relation's columns; -1 when binding could not list them.</param>
    /// <param name="scope">The scope it is read in, which holds what the source's relation holds.</param>
    /// <param name="at">Where it is read.</param>
    private static Value FromLocal(BoundSource source, int ordinal, ReadScope scope, SourceLocation at)
    {
        var values = scope.Of(source);

        // Reported where it is read, as a binding problem is; the detail names the statement that caused it.
        var value = values.Why is { } why
            ? Value.Unknown(why with { At = at })
            : ordinal >= 0 && values.Values is { } all ? all[ordinal]
            : throw new InvalidOperationException($"Binding could not list the columns of {Text(source.Syntax)}, whose values are known.");
        if (value.Shape is not UnknownShape && ordinal >= 0 && scope.Facts.Proved(source, new RelationColumnKey(ordinal)) != Facts.Proof.None)
            value = value.WithNullableBecause([]);
        if (source.Optional is { } optional && !scope.Facts.Matched(source) && !AlwaysApplied(source, scope))
            value = value.WithNullableBecause(NullReasons.Join(value.NullableBecause, [new IrNullReason(optional.IsApplied ? IrNullRule.UnmatchedLateralRow : IrNullRule.UnmatchedRow, optional.At.ToString())]));
        if (Unmatched(source, scope) is { } unmatched && value.Shape is not UnknownShape)
            value = value.WithNullableBecause(NullReasons.Join(value.NullableBecause, [unmatched]));
        if (Rolled(source, scope) is { } rolled && value.Shape is not UnknownShape)
            value = value.WithNullableBecause(NullReasons.Join(value.NullableBecause, [rolled]));

        // A CASE branch that tested this very value proves it, whatever made it nullable (Typer.Case.cs).
        return value.Shape is not UnknownShape && ordinal >= 0 && scope.Facts.Proved(source, new RelationColumnKey(ordinal)) == Facts.Proof.OfTheValue
            ? value.WithNullableBecause([])
            : value;
    }

    /// <summary>
    /// A derived table, <c>FROM (SELECT …) AS d (columns)</c>: read like a CTE of its own, so its values pass through
    /// unchanged and keep what its query proves of them. It may read the enclosing queries' columns, and an APPLY's left
    /// input's when it is (or is inside) the APPLY's right side, as its query was bound. Its row count is its query's
    /// (Cardinality.cs), where it is read: for an APPLY's right side, for each outer row.
    /// </summary>
    private LocalRelationValues Derived(DerivedTable derived, ReadScope scope)
    {
        var at = At(derived.Syntax);
        var name = derived.Name.Length > 0 ? derived.Name : "(derived table)";
        var row = Query(derived.Query, scope);
        return Holds(derived, row) is { } relation
            ? relation with { Rows = row.Rows, AtLeastOne = row.Rows == IrCardinality.One }
            : Unlisted(name, row, at);
    }

    /// <summary>
    /// An integer literal within int's range is an int, never null; a larger one is numeric of its own digits (observed:
    /// A_literals_type_is_its_own_digits_or_characters). The parser gives <c>-2147483648</c> as one literal, which is int's
    /// least value, an int (observed in the same test).
    /// </summary>
    private Value Integer(IntegerLiteral literal)
    {
        if (long.TryParse(literal.Value, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var n) && n is >= int.MinValue and <= int.MaxValue)
        {
            var own = literal.Value.TrimStart('-', '+').TrimStart('0').Length is var d and > 0 ? d : 1;
            return new Value(SqlTypes.ToConvertedShape(ScalarType.Of(BuiltInType.Int), At(literal)), []) { Digits = IntDigits.Literal(own) };
        }

        var digits = literal.Value.TrimStart('-', '+').TrimStart('0').Length;
        return digits <= 38
            ? new Value(SqlTypes.ToConvertedShape(new ScalarType(BuiltInType.Numeric, Precision: digits), At(literal)), [])
            : Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"The literal {literal.Value} has more than 38 digits.", At(literal)));
    }
}
