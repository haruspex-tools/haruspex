using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// Set operations (UNION, UNION ALL, INTERSECT, EXCEPT and parenthesised queries) and OPENJSON. README.md in this
// folder ("Set operations", "OPENJSON") gives the rules and the observations they rest on (SetJoinObservationTests in the
// differential tests). CROSS JOIN and FULL OUTER JOIN are binding's (Binder.cs), refinement's and cardinality's.
internal sealed partial class ProcedureAnalyzer
{
    /// <summary>
    /// The row any query expression gives: a query specification's, or a set operation's over the queries it combines. Each
    /// query is read on its own, with its own filters, so what its WHERE proves holds of its own columns only.
    /// </summary>
    private Row Query(QueryExpression query, BindingScope? parent)
    {
        if (query is QuerySpecification specification)
            return Query(specification, parent);

        // A chain of set operations, which the parser gives left to right; parentheses group a query of their own.
        var members = new List<QueryExpression>();
        var operators = new List<BinaryQueryExpression>();
        Flatten(query, members, operators);
        var rows = members.Select(m => m is QueryParenthesisExpression p ? Parenthesised(p, parent) : Query(m, parent)).ToList();
        return Limited(rows.Count == 1 ? rows[0] : Combine(rows, operators, At(query)), OffsetOf(query));
    }

    /// <summary>
    /// The FOR clause of a query expression. A set operation's ORDER BY, OFFSET and FOR belong to the whole of it, but the
    /// parser puts them on its last query when it is a subquery or a derived table, and on the set operation itself only when
    /// it is a statement's; so they are looked for in both places. A query in parentheses keeps its own. (SQL Server refuses
    /// FOR on a set operation anywhere but a statement, 1086, observed: A_for_json_subquery_over_a_set_operation_needs_a_derived_table;
    /// so in a subquery this only stops the last query being read as a plain one.)
    /// </summary>
    private static ForClause? ForOf(QueryExpression query) => query.ForClause ?? (query is BinaryQueryExpression ? Last(query)?.ForClause : null);

    /// <summary>The OFFSET clause of a query expression; see <see cref="ForOf"/>.</summary>
    /// <remarks>
    /// Read on the last query too, where it also bounds that query alone. That only claims less: a FETCH of at most one row
    /// bounds the whole as well, and OFFSET alone only makes a query of one row at most one.
    /// </remarks>
    private static OffsetClause? OffsetOf(QueryExpression query) => query.OffsetClause ?? (query is BinaryQueryExpression ? Last(query)?.OffsetClause : null);

    private static QuerySpecification? Last(QueryExpression query) => query switch
    {
        BinaryQueryExpression binary => Last(binary.SecondQueryExpression),
        QuerySpecification specification => specification,
        _ => null,
    };

    /// <summary>A parenthesised query: its query's row, under its own OFFSET … FETCH when it has one.</summary>
    private Row Parenthesised(QueryParenthesisExpression parenthesis, BindingScope? parent) =>
        Limited(Query(parenthesis.QueryExpression, parent), parenthesis.OffsetClause);

    private static Row Limited(Row row, OffsetClause? offset) => offset is null ? row : row with { Rows = Limited(row.Rows, offset) };

    /// <summary>
    /// A set operation's row from its queries' rows (<paramref name="rows"/>) and the operators
    /// between them, in the order written.
    /// <list type="bullet">
    /// <item>Names are the first query's; each column's type is the one of highest precedence over every query's at once, an
    /// integer literal with its own digits, as COALESCE's (observed:
    /// A_chain_of_set_operations_is_typed_over_all_its_queries_at_once_so_an_integer_literal_keeps_its_digits); a NULL literal
    /// adds nothing but its null.</item>
    /// <item>INTERSECT binds tighter than UNION and EXCEPT, which apply left to right (observed:
    /// Intersect_binds_tighter_than_union_and_except_whatever_the_parser_shows); the parser gives the chain left to right, so
    /// it is regrouped here.</item>
    /// <item>Which queries' values come out, and what can be null: UNION and UNION ALL every query's, null if any is;
    /// INTERSECT the left side's, never null when either side is never null, since NULLs compare equal there and a null left
    /// value needs a null on the right; EXCEPT the left side's, null as it is (observed:
    /// Intersect_and_except_treat_nulls_as_equal_so_intersect_with_a_non_null_side_has_no_null,
    /// Intersect_and_except_return_the_left_sides_values_and_compare_under_the_collation).</item>
    /// <item>Rows: UNION of anything may be many; INTERSECT at most the fewer side's, EXCEPT at most the left side's. No key
    /// survives (<see cref="Row.Sources"/> is null).</item>
    /// </list>
    /// </summary>
    private Row Combine(List<Row> rows, List<BinaryQueryExpression> operators, SourceLocation at)
    {
        if (rows.FirstOrDefault(r => r.Star is not null) is { } starred)
            return new Row([], starred.Star);
        var count = rows[0].Columns.Count;
        if (rows.Any(r => r.Columns.Count != count))
        {
            // SQL Server refuses it (205), so the procedure is never created; nothing to describe.
            var refused = Value.Unknown(Unsupported(IrUnknownReason.UnsupportedQuery, "The queries of this set operation list different numbers of columns, which SQL Server refuses.", at));
            return new Row([.. rows[0].Columns.Select(c => c with { Value = refused, Origin = null })], null);
        }

        // Regroup: INTERSECT first, then UNION and EXCEPT left to right. Each node says which queries' values come out of it.
        var nodes = Enumerable.Range(0, rows.Count).Select(i => new SetNode([i], null, null, false, rows[i].Rows)).ToList();
        var pending = operators.Select(o => o.BinaryQueryExpressionType).ToList();
        for (var i = 0; i < pending.Count;)
        {
            if (pending[i] == BinaryQueryExpressionType.Intersect)
            {
                nodes[i] = Join(nodes[i], nodes[i + 1], BinaryQueryExpressionType.Intersect);
                nodes.RemoveAt(i + 1);
                pending.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }

        var node = nodes[0];
        for (var i = 0; i < pending.Count; i++)
            node = Join(node, nodes[i + 1], pending[i]);

        var columns = new List<RowColumn>();
        for (var c = 0; c < count; c++)
        {
            var first = rows[0].Columns[c];
            columns.Add(new RowColumn(first.Name, SetColumn([.. rows.Select(r => r.Columns[c])], node, first.At), first.At));
        }

        return new Row(columns, null, node.Rows);
    }

    /// <summary>
    /// One step of a set operation: which queries' values come out (<paramref name="Out"/>), and the tree it was built from,
    /// for nulls.
    /// </summary>
    private sealed record SetNode(IReadOnlyList<int> Out, SetNode? Left, SetNode? Right, bool Intersect, IrCardinality Rows);

    private static SetNode Join(SetNode left, SetNode right, BinaryQueryExpressionType type) => type switch
    {
        BinaryQueryExpressionType.Intersect => new SetNode(left.Out, left, right, true, left.Rows != IrCardinality.Many || right.Rows != IrCardinality.Many ? IrCardinality.AtMostOne : IrCardinality.Many),
        BinaryQueryExpressionType.Except => new SetNode(left.Out, left, null, false, left.Rows != IrCardinality.Many ? IrCardinality.AtMostOne : IrCardinality.Many),
        _ => new SetNode([.. left.Out, .. right.Out], left, right, false, IrCardinality.Many),
    };

    /// <summary>Why a column of a set operation's node may be null, given each query's own reasons.</summary>
    private static EquatableArray<IrNullReason> SetNulls(SetNode node, IReadOnlyList<EquatableArray<IrNullReason>> because)
    {
        if (node.Left is null)
            return because[node.Out[0]];
        var left = SetNulls(node.Left, because);
        if (node.Right is null)
            return left;
        var right = SetNulls(node.Right, because);
        if (node.Intersect)
            return left.Count == 0 || right.Count == 0 ? [] : left;
        return NullReasons.Join(left, right);
    }

    /// <summary>One column of a set operation, from that column of every query (see <see cref="Combine(List{Row}, List{BinaryQueryExpression}, SourceLocation)"/>).</summary>
    private Value SetColumn(List<RowColumn> columns, SetNode node, SourceLocation at)
    {
        static bool IsNull(RowColumn c) => c.Expression is { } e && Strip(e) is NullLiteral;
        var given = columns.Where(c => !IsNull(c)).ToList();
        if (given.Count == 0)
            return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, "Every query of this set operation gives NULL in this column.", at));
        if (given.FirstOrDefault(c => c.Value.Shape is UnknownShape) is { } unknown)
            return unknown.Value;

        // JSON comes out of a set operation as text, but json_query's stays JSON (observed:
        // Json_through_a_set_operation_is_text_except_json_query), which a value here does not record.
        if (given.Any(c => c.Value.IsLiveJson))
            return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, "A set operation over JSON is not modelled: whether it comes out as JSON or text depends on how it was made.", at));

        // Text holding JSON stays text; its content is known when every query's is the same.
        var output = node.Out.Select(i => columns[i]).Where(c => !IsNull(c)).ToList();
        if (given.All(c => c.Value.Shape is JsonTextShape))
        {
            var because = SetNulls(node, [.. columns.Select(c => IsNull(c) ? [new IrNullReason(IrNullRule.NullLiteral, c.At.ToString())] : c.Value.NullableBecause)]);
            var contents = output.Select(c => ((JsonTextShape)c.Value.Shape).Content).ToList();
            var same = contents.Count > 0 && contents.All(x => x is not null && x == contents[0]);
            return new Value(new JsonTextShape(same ? contents[0] : null), because);
        }

        // Each query's value is converted to the column's type, which may overflow, NULL under the caller's options; that is
        // one more reason that query's value may be null, before INTERSECT and EXCEPT decide whose nulls come out.
        var combined = Chained([.. given.Select(c => (c.Value, c.Expression ?? new NullLiteral(), c.At))]);
        if (combined.Type is not { } type)
            return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"This set operation's column: {combined.Why}", at));
        var next = 0;
        var reasons = SetNulls(node, [.. columns.Select(c => IsNull(c)
            ? [new IrNullReason(IrNullRule.NullLiteral, c.At.ToString())]
            : NullReasons.Join(c.Value.NullableBecause, combined.Overflows[next++]))]);

        // When every query whose values come out is of the column's type already, nothing is converted, and the facets they
        // all have hold. Otherwise those of a query whose every value converts unchanged (ScalarType.Preserves: an int into a
        // bigint or a decimal holding it, a varchar into a longer one) still hold, and a query whose values may change (rounded,
        // made approximate, padded, cut short) brings only the column type's: so a range or a length widens to cover every
        // query, and is never narrower than the type's.
        Value value;
        if (output.Count > 0 && output.All(c => c.Value.Shape is ScalarShape { Type.Sql: var sql } && sql == type.Text))
        {
            value = output.Skip(1).Aggregate(output[0].Value, (a, b) => Value.Join(a, b.Value, at, "this set operation's column")) with { NullableBecause = reasons };
            if (value.Shape is UnknownShape)
                value = Typed(type, reasons, at);
        }
        else
        {
            value = Typed(type, reasons, at);
            if (output.Count > 0 && value.Shape is ScalarShape typed)
            {
                var each = output.Select(c => c.Value.Shape is ScalarShape { Type: var own } && ScalarType.Parse(own.Sql) is { } from && ScalarType.Preserves(from, type)
                    ? own.Facets
                    : typed.Type.Facets).ToList();
                value = value with { Shape = typed with { Type = typed.Type with { Facets = ScalarFacets.Widened(each, typed.Type.Facets) } } };
            }
        }

        // A set operation is not folded to a constant: an int column of one counts 10 digits (observed:
        // An_int_constant_counts_its_own_digits_beside_a_decimal).
        value = value with { Digits = null };
        return value.Shape is ScalarShape scalar
            ? value with { Shape = scalar with { Type = scalar.Type with { Facets = WithValues(scalar.Type.Facets, SetValues(output, type, scalar.Type.Sql)) } } }
            : value;
    }

    private Value Typed(ScalarType type, EquatableArray<IrNullReason> because, SourceLocation at) =>
        new(SqlTypes.ToConvertedShape(model, type.ToSqlType(), at), because);

    private static IrFacets? WithValues(IrFacets? facets, IrValueSet? set)
    {
        var with = (facets ?? new IrFacets()) with { Values = set };
        return with.IsEmpty ? null : with;
    }

    /// <summary>
    /// The closed set of a set operation's column: the union of the sets of the queries whose values come out, each a CHECK's
    /// set the value carries or the literal it is; none when any has none. Sets of different kinds are not merged; a
    /// fixed-length type would pad a shorter value (observed: A_fixed_length_string_pads_a_shorter_one_through_a_set_operation),
    /// so there only queries already of that type count. A UNION that removes duplicates under the collation keeps one of
    /// them, which is still one of the items.
    /// </summary>
    private IrValueSet? SetValues(List<RowColumn> output, ScalarType type, string? sql)
    {
        if (output.Count == 0)
            return null;
        if (type.Name is "char" or "nchar" && output.Any(c => c.Value.Shape is not ScalarShape { Type.Sql: var own } || own != sql))
            return null;

        var sets = new List<IrValueSet>();
        foreach (var column in output)
        {
            var own = (column.Value.Shape as ScalarShape)?.Type;
            var set = own?.Facets?.Values;
            if (set is not null && !Keeps(set, ScalarType.Parse(own!.Sql), type))
                return null;
            set ??= column.Expression is { } expression ? LiteralSet([expression], type, expression) : null;
            if (set is null)
                return null;
            sets.Add(set);
        }

        if (sets.Select(s => s.Match).Distinct().Count() > 1 || sets.Select(s => s.Items.Any(i => i.IsString)).Distinct().Count() > 1)
            return null;

        // Strings ordinally, numbers in their numeric order, each once, as a CASE's literals are.
        var items = sets.SelectMany(s => s.Items).ToList();
        IReadOnlyList<IrValue> merged;
        if (items.Any(i => i.IsString))
        {
            merged = [.. items.DistinctBy(i => i.Text, StringComparer.Ordinal).OrderBy(i => i.Text, StringComparer.Ordinal)];
        }
        else
        {
            var numbers = new List<(ExactDecimal Number, IrValue Item)>();
            foreach (var item in items)
            {
                if (!ExactDecimal.TryParse(item.Text, out var number))
                    return null;
                if (!numbers.Any(n => n.Number.CompareTo(number) == 0))
                    numbers.Add((number, item));
            }

            merged = [.. numbers.OrderBy(n => n.Number).Select(n => n.Item)];
        }

        return new IrValueSet([.. merged], sets[0].Match, [.. sets.SelectMany(s => s.From).Distinct()]);
    }

    /// <summary>
    /// Whether a query's closed set still holds of its values converted to the column's type: strings into a string type long
    /// enough for each (not cut short; a fixed-length type is ruled out above), numbers into a type that keeps every value
    /// (<see cref="ScalarType.Preserves"/>: not rounded, not made approximate). A string's set never holds of a number it is
    /// converted to ('01' is 1), nor a number's of a string.
    /// </summary>
    private static bool Keeps(IrValueSet set, ScalarType? from, ScalarType type)
    {
        if (from is null)
            return false;
        if (set.Items.Any(i => i.IsString))
            return from.IsString && type.IsString && (type.IsMax || set.Items.All(i => i.Text.Length <= type.Length));
        return ScalarType.Preserves(from, type);
    }

    // ---- OPENJSON ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// An <c>OPENJSON</c> call's rows. Without <c>WITH</c>: <c>key</c> nvarchar(4000) and <c>type</c> tinyint, never null, and
    /// <c>value</c> nvarchar(max), NULL for a JSON null; <c>type</c> is 0 to 5 (observed:
    /// Openjson_without_with_gives_key_value_and_type). With <c>WITH</c>: each column of its declared type, the value converted
    /// to it as CAST does (a string cut short), and NULL for a JSON null even under a <c>strict</c> path, which only turns a
    /// missing property into an error (observed: Openjson_with_columns_are_of_their_declared_types_and_null_for_a_missing_path_or_a_json_null,
    /// Openjson_with_a_strict_path_that_is_missing_is_an_error_not_a_null). An <c>AS JSON</c> column holds an object or array,
    /// which FOR JSON nests as JSON, whose shape is in the data (observed:
    /// Openjson_as_json_is_nvarchar_max_holding_an_object_or_array_that_nests_as_json). How many rows is the data's.
    /// </summary>
    private LocalRelationValues OpenJsonRelation(OpenJsonTableReference reference, BindingScope? outer)
    {
        var at = At(reference);
        var name = reference.Alias?.Value ?? "OPENJSON";
        static IrNullReason JsonValue(SourceLocation where) => new(IrNullRule.JsonNullOrAbsent, where.ToString());

        // Over JSON built here its shape says more (ProcedureAnalyzer.JsonReads.cs).
        if (TracedOpenJson(reference, outer ?? NoFrom(), at) is { } traced)
        {
            List<string> columns = reference.SchemaDeclarationItems.Count == 0 ? ["key", "value", "type"] : [.. reference.SchemaDeclarationItems.Select(i => i.ColumnDefinition.ColumnIdentifier.Value)];
            return columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count
                ? LocalRelationValues.Unknown(Unsupported(IrUnknownReason.UnsupportedQuery, $"{name}: its column names are not distinct.", at))
                : new LocalRelationValues([.. columns], [.. traced], null);
        }

        if (reference.SchemaDeclarationItems.Count == 0)
        {
            var type = new IrValueSet(
                [.. Enumerable.Range(0, 6).Select(n => new IrValue(n.ToString(System.Globalization.CultureInfo.InvariantCulture), IsString: false))],
                IrValueMatch.Exact,
                [new IrFactSource(IrFactSourceKind.Engine, "OPENJSON type")]);
            var tinyint = (ScalarShape)SqlTypes.ToConvertedShape(model, new SqlType(SqlTypeKind.BuiltIn, "tinyint", []), at);
            return new LocalRelationValues(
                ["key", "value", "type"],
                [
                    new Value(SqlTypes.ToConvertedShape(model, new SqlType(SqlTypeKind.BuiltIn, "nvarchar", ["4000"]), at), []),
                    new Value(SqlTypes.ToConvertedShape(model, new SqlType(SqlTypeKind.BuiltIn, "nvarchar", ["max"]), at), [JsonValue(at)]),
                    new Value(tinyint with { Type = tinyint.Type with { Facets = WithValues(tinyint.Type.Facets, type) } }, []),
                ],
                null);
        }

        var names = new List<string>();
        var values = new List<Value>();
        foreach (var item in reference.SchemaDeclarationItems)
        {
            var column = item.ColumnDefinition;
            var where = At(column);
            names.Add(column.ColumnIdentifier.Value);
            if (item is SchemaDeclarationItemOpenjson { AsJson: true })
            {
                values.Add(new Value(
                    Unsupported(IrUnknownReason.JsonShapeUnknown, $"{name}.{column.ColumnIdentifier.Value} is JSON read from a document: its shape is in the data.", where),
                    [JsonValue(where)],
                    IsLiveJson: true));
                continue;
            }

            values.Add(new Value(Unpadded(SqlTypes.ToConvertedShape(model, SqlType.From(column.DataType), where)), [JsonValue(where)]));
        }

        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            return LocalRelationValues.Unknown(Unsupported(IrUnknownReason.UnsupportedQuery, $"{name}: its column names are not distinct.", at));
        return new LocalRelationValues([.. names], [.. values], null);
    }
}
