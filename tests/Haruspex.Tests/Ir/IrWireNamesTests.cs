using System.Runtime.CompilerServices;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// Each closed vocabulary's names in the IR's JSON, all of them: a name is part of the format, so a change to one is a change
/// to the format, which these tests show even for a value no fixture produces. Within a vocabulary no two values share a name.
/// </summary>
public class IrWireNamesTests
{
    /// <summary>The names of every value of a vocabulary, in the order its members are declared, after checking none repeats.</summary>
    private static List<string> Names<T>(Func<T, string> name, params T[] without)
        where T : struct, Enum
    {
        var names = Enum.GetValues<T>().Except(without).Select(name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        return names;
    }

    [Fact]
    public void Scalar_kinds_keep_their_names() => Assert.Equal(
        ["string", "number", "boolean", "date", "time", "datetime", "uuid", "bytes"],
        Names<IrScalarKind>(IrWireNames.Of));

    [Fact]
    public void Formats_keep_their_names() => Assert.Equal(
        ["uint8", "int16", "int32", "int64", "decimal", "float", "local", "offset"],
        Names<IrFormat>(IrWireNames.Of));

    [Fact]
    public void Object_kinds_keep_their_names() => Assert.Equal(
        ["procedure", "query"],
        Names<IrObjectKind>(IrWireNames.Of));

    [Fact]
    public void Type_owners_keep_their_names() => Assert.Equal(
        ["procedure", "function", "query", "tableType"],
        Names<IrTypeOwner>(IrWireNames.Of));

    [Fact]
    public void Column_generations_keep_their_names() => Assert.Equal(
        ["identity", "computed", "rowversion"],
        Names<IrColumnGeneration>(IrWireNames.Of));

    [Fact]
    public void Encodings_keep_their_names() => Assert.Equal(
        ["rowset", "json"],
        Names<IrEncoding>(IrWireNames.Of));

    [Fact]
    public void Cardinalities_keep_their_names() => Assert.Equal(
        ["many", "atMostOne", "one"],
        Names<IrCardinality>(IrWireNames.Of));

    [Fact]
    public void Presences_keep_their_names() => Assert.Equal(
        ["always", "omittedWhenNull", "optional"],
        Names<IrPresence>(IrWireNames.Of));

    [Fact]
    public void Null_rules_keep_their_names() => Assert.Equal(
        ["nullable-column", "unmatched-row", "unmatched-lateral-row", "empty-subquery", "all-fields-null", "empty-aggregate", "function-result", "parameter", "null-input", "no-rows", "not-run", "variable", "count-overflow", "arithmetic-overflow", "precision-loss", "failed-conversion", "subtotal-row", "missing-for-action", "no-branch-matched", "null-literal", "out-of-range", "read-twice", "null-when-equal", "json-null-or-absent", "empty-frame", "window-edge", "domain-error", "too-few-values", "unknown", "declared", "not-in-both"],
        Names<IrNullRule>(IrWireNames.Of));

    [Fact]
    public void Unknown_reasons_keep_their_names() => Assert.Equal(
        ["unsupported-statement", "external-code", "redefined", "unsupported-query", "unsupported-expression", "unsupported-source", "unsupported-sql-type", "unresolved-table", "unresolved-column", "unnamed-column", "duplicate-column", "unresolved-recursion", "json-shape-unknown", "caller-temp-table", "varying-results", "dynamic-sql", "procedure-variable", "system-procedure", "missing-procedure", "ambiguous-procedure", "trigger-results", "hidden-column", "encrypted-definition", "sqlcmd-variable", "undecided-parameter"],
        Names<IrUnknownReason>(IrWireNames.Of));

    [Fact]
    public void Override_settings_keep_their_names() => Assert.Equal(
        ["not-null", "type", "exact-values", "results"],
        Names<IrOverrideSetting>(IrWireNames.Of));

    [Fact]
    public void Fact_source_kinds_keep_their_names() => Assert.Equal(
        ["type", "check", "engine", "expression"],
        Names<IrFactSourceKind>(IrWireNames.Of));

    [Fact]
    public void Length_units_keep_their_names() => Assert.Equal(
        ["utf16", "bytes"],
        Names<IrLengthUnit>(IrWireNames.Of));

    [Fact]
    public void Byte_encodings_keep_their_names() => Assert.Equal(
        ["single-byte", "utf-8", "double-byte"],
        Names(IrWireNames.Of, IrByteEncoding.Unknown));

    [Fact]
    public void Value_matches_keep_their_names() => Assert.Equal(
        ["exact", "collation", "padded", "canonical"],
        Names<IrValueMatch>(IrWireNames.Of));

    [Fact]
    public void An_unknown_byte_encoding_has_no_name_since_the_writer_leaves_it_out() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => IrWireNames.Of(IrByteEncoding.Unknown));

    [Fact]
    public void A_value_that_is_no_member_has_no_name() =>
        Assert.Throws<SwitchExpressionException>(() => IrWireNames.Of((IrNullRule)999));
}
