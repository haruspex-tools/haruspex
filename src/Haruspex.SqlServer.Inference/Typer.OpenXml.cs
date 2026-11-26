using System.Collections.Frozen;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Syntax;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// <c>OPENXML(handle, 'pattern' [, flags]) WITH (…)</c>, a row for each node its pattern finds in a document
/// <c>sp_xml_preparedocument</c> parsed. Every rule was observed on SQL Server 2025 CU10 (<c>XmlExpressionObservationTests</c>).
/// Why: docs/decisions/0137-xml-s-nodes-query-for-xml-and-openxml-are-typed-as-the-server-gives-them.md.
/// </summary>
internal sealed partial class Typer
{
    /// <summary>The families of a <c>WITH</c> column's type that are read here: each observed to be the column's type, its value converted from the text found.</summary>
    private static readonly FrozenSet<TypeFamily> OpenXmlFamilies = new[]
    {
        TypeFamily.String, TypeFamily.Integer, TypeFamily.Decimal, TypeFamily.Money, TypeFamily.Approximate, TypeFamily.Bit,
        TypeFamily.DateAndTime, TypeFamily.Uuid, TypeFamily.Binary, TypeFamily.Xml,
    }.ToFrozenSet();

    /// <summary>
    /// An <c>OPENXML</c> call's rows. With <c>WITH (…)</c>: each column of its declared type (observed:
    /// Openxml_with_columns_are_their_declared_types_converted_as_cast_converts_text), its value the text its path finds
    /// converted as CAST converts <c>nvarchar</c> (a string cut short silently, a <c>char</c> padded, a decimal rounded, text that
    /// doesn't convert an error, an overflow an error or NULL as the caller's options make it), and NULL where the path finds
    /// nothing (<c>xml-value</c>). With <c>WITH</c> a table's name its columns are that table's, read the same way, so NULL where
    /// the path finds nothing even where the table declares <c>NOT NULL</c> (observed:
    /// Openxml_with_a_table_s_name_is_null_where_its_path_finds_nothing_whatever_the_table_declares); a table named without its
    /// schema, and a computed or hidden column, or one of its own collation, aren't read. Without <c>WITH</c> it is the edge table,
    /// which isn't modelled. How many rows is the document's.
    /// </summary>
    /// <param name="symbol">The call, bound.</param>
    private LocalRelationValues OpenXmlRelation(OpenXmlCall symbol)
    {
        var reference = symbol.Syntax;
        var at = At(reference);
        LocalRelationValues Fails(string detail) => LocalRelationValues.Unknown(Unsupported(IrUnknownReason.UnsupportedSource, $"{symbol.Name}: {detail}", at));

        if (reference.TableName is not null && symbol.Table is not { } table)
            return Fails("OPENXML WITH a table's name is read only for a table of the project named with its schema.");
        if (reference.TableName is null && reference.SchemaDeclarationItems.Count == 0)
            return Fails("OPENXML without WITH gives the edge table, a row for each node of the document and its parts, which is not modelled.");
        if (symbol.Columns is not { } names)
            return Fails("its column names are not distinct.");

        // What the path finds is text, converted to the column's type as CAST converts it (observed). NULL where it finds none,
        // at the call (the rule's at, as for value()).
        var text = Value.Computed(SqlTypes.ToConvertedShape(new ScalarType(BuiltInType.NVarChar, -1), at), []);
        EquatableArray<IrNullReason> missing = [new IrNullReason(IrNullRule.XmlValue, at.ToString())];
        Value Column(string name, SqlType? declared, SourceLocation where)
        {
            var shape = SqlTypes.ToConvertedShape(model, declared, where);
            if (shape is UnknownShape unknown)
                return Value.Unknown(unknown);
            if (declared is { Collection: not null } || shape is not ScalarShape { BuiltIn: { } type } || !OpenXmlFamilies.Contains(type.Family))
                return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedSqlType, $"{symbol.Name}.{name}: an OPENXML column of the type {declared} is not modelled.", where));
            return Value.Computed(shape, NullReasons.Join(missing, Converting(text, shape, where, summaries.Session)));
        }

        List<Value> values = symbol.Table is { } named
            ? [.. named.Columns.Select(c => c.IsComputed || c.IsHidden || c.Collation is not null
                ? Value.Unknown(Unsupported(IrUnknownReason.UnsupportedSqlType, $"{symbol.Name}.{c.Name}: an OPENXML column taken from a computed or hidden column, or one of its own collation, is not modelled.", c.At))
                : Column(c.Name, c.Type, c.At))]
            : [.. reference.SchemaDeclarationItems.Select(i => Column(i.ColumnDefinition.ColumnIdentifier.Value, SyntaxReader.Type(i.ColumnDefinition.DataType, search.Own), At(i.ColumnDefinition)))];
        return new LocalRelationValues(names, [.. values], null);
    }
}
