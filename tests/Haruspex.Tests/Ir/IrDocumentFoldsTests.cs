using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// The folds a document names (<see cref="IrDocumentFolds"/>): a canonical value set names its fold wherever its type stands, so the
/// table of folds holds each, or the reader refuses the file the tool wrote. A table type with an identity, computed or rowversion
/// column has no row type, so there its column's type is the only place the set stands.
/// </summary>
public class IrDocumentFoldsTests
{
    private const string Fold = "Latin1_General_100_CI_AS.codepage";

    private static readonly IrScalar State = new(IrScalarKind.String, null, "varchar(10)", new IrFacets(
        Values: new IrValueSet([new IrValue("OPEN", IsString: true), new IrValue("HELD", IsString: true)], IrValueMatch.Canonical, [new IrFactSource(IrFactSourceKind.Check, "ck_state")], Fold)));

    private static readonly IrDocument Empty = new(new IrSource("tsql"), [], new Dictionary<string, IrObjectType>(), []);

    private static IrObject Procedure(EquatableArray<IrParameter> parameters, EquatableArray<IrResult>? results) =>
        new("depot.p", "p", IrObjectKind.Procedure, "procedures/p.sql:1:1", parameters, results);

    /// <summary>A document whose one canonical set stands in <paramref name="place"/>, nested in an array where it can be.</summary>
    private static IrDocument WithSetIn(string place) => place switch
    {
        "parameter" => Empty with { Objects = [Procedure([new IrParameter("@states", new IrArray(State), HasDefault: false, IsOutput: false)], [])] },
        "result" => Empty with { Objects = [Procedure([], [new IrResult(IrEncoding.Json, new IrArray(State), [])])] },
        "named type's field" => Empty with { Types = new Dictionary<string, IrObjectType> { ["Row"] = new([new IrField("state", State, IrPresence.Always, [])]) } },
        "table type's column" => Empty with
        {
            // A type with an identity column has no row type, so the parameter that takes it is unknown.
            Objects = [Procedure([new IrParameter("@crates", new IrUnknownType(IrUnknownReason.UnsupportedSqlType), HasDefault: false, IsOutput: false) { TableType = "depot.crate_list" }], [])],
            Unknowns = [new IrUnknown("depot.p", "parameters.@crates", IrUnknownReason.UnsupportedSqlType, "The table type depot.crate_list has an identity column.", "procedures/p.sql:2:5")],
            TableTypes = new Dictionary<string, IrTableType>
            {
                ["depot.crate_list"] = new(
                    "depot",
                    "crate_list",
                    [
                        new IrTableColumn("serial", "int", Nullable: false) { Generated = IrColumnGeneration.Identity },
                        new IrTableColumn("state", "varchar(10)", Nullable: false) { Type = State, Collation = "Latin1_General_100_CI_AS" },
                    ],
                    "types/crate_list.sql:1:1"),
            }.ToEquatableMap(t => t.Key, t => t.Value),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(place), place, null),
    };

    [Theory]
    [InlineData("parameter")]
    [InlineData("result")]
    [InlineData("named type's field")]
    [InlineData("table type's column")]
    public void A_canonical_set_names_its_fold_wherever_its_type_stands(string place) =>
        Assert.Equal([Fold], IrDocumentFolds.Named(WithSetIn(place)));

    [Theory]
    [InlineData("parameter")]
    [InlineData("result")]
    [InlineData("named type's field")]
    [InlineData("table type's column")]
    public void A_settled_document_keeps_the_fold_and_reads_back_equal(string place)
    {
        var fold = new IrFold("\u0000", new Dictionary<string, string> { ["O"] = "Oo" }.ToEquatableMap(c => c.Key, c => c.Value));

        var settled = IrDocumentFolds.Settled(WithSetIn(place), [new Dictionary<string, IrFold> { [Fold] = fold }.ToEquatableMap(f => f.Key, f => f.Value)]);

        Assert.Equal([Fold], settled.Folds.Keys);
        Assert.Equal(settled, IrJsonReader.Read(IrJsonWriter.Write(settled)));
    }
}
