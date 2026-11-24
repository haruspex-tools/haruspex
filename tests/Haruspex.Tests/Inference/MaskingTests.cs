using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// Masked columns: a caller without <c>UNMASK</c> reads a masked column's mask, and the mask of any value computed from one, so of
/// those only the type, whether it may be null and the greatest length are claimed; a session declared to hold <c>UNMASK</c> claims
/// the rest again (observed in the differential tests' MaskingObservationTests;
/// docs/decisions/0118-a-masked-column-claims-only-its-type-nullability-and-greatest-length.md).
/// </summary>
public partial class MaskingTests
{
    /// <summary>
    /// A clinic's patients: each masked column beside an unmasked twin of the same declaration, so that what the mask takes away is
    /// what differs between the two.
    /// </summary>
    private const string Tables = """
        CREATE TABLE clinic.patient (
            id int NOT NULL PRIMARY KEY,
            rating int MASKED WITH (FUNCTION = 'default()') NOT NULL CHECK (rating BETWEEN 1 AND 5),
            rating_plain int NOT NULL CHECK (rating_plain BETWEEN 1 AND 5),
            band int MASKED WITH (FUNCTION = 'random(1, 3)') NOT NULL CHECK (band IN (1, 2, 3)),
            band_plain int NOT NULL CHECK (band_plain IN (1, 2, 3)),
            code char(6) MASKED WITH (FUNCTION = 'default()') NOT NULL,
            code_plain char(6) NOT NULL,
            note varchar(10) MASKED WITH (FUNCTION = 'partial(1, "XX", 0)') NULL CHECK (LEN(note) >= 2),
            note_plain varchar(10) NULL CHECK (LEN(note_plain) >= 2),
            email nvarchar(40) MASKED WITH (FUNCTION = 'email()') NOT NULL,
            seen date MASKED WITH (FUNCTION = 'default()') NULL);
        GO
        """;

    private static IrDocument Procedure(string body, SessionOptions? session = null, params (string File, string Sql)[] more) =>
        InferProcedure.Infer("clinic.p", session ?? SessionOptions.NotKnown, [("tables.sql", Tables), .. more, ("p.sql", $"CREATE PROCEDURE clinic.p AS\n{body}")]);

    private static IrScalar Scalar(IrDocument doc, string name) => (IrScalar)Field(doc, name).Type;

    private static IrFacets? Facets(IrDocument doc, string name) => Scalar(doc, name).Facets;

    /// <summary>The facets of the type alone, as a masked column of it keeps them.</summary>
    private static IrFacets? TypeOnly(IrFacets? facets) =>
        facets is null ? null : new IrFacets(
            facets.Length is { From.Kind: IrFactSourceKind.Type } l ? l with { IsFixed = false } : null,
            null,
            facets.Digits is { From.Kind: IrFactSourceKind.Type } d ? d : null,
            facets.Min is { From.Kind: IrFactSourceKind.Type } min ? min : null,
            facets.Max is { From.Kind: IrFactSourceKind.Type } max ? max : null,
            null) is { IsEmpty: false } kept ? kept : null;

    [Fact]
    public void A_masked_column_claims_its_type_whether_it_may_be_null_and_its_greatest_length_only()
    {
        var doc = Procedure("SELECT rating, rating_plain, band, band_plain, code, code_plain, note, note_plain, email, seen FROM clinic.patient;");

        // The twins differ only by the mask: a range, a value set, a fixed length and a minimum length go, the type's own stay.
        Assert.Equal(IrFactSourceKind.Check, Facets(doc, "rating_plain")!.Min!.From.Kind);
        Assert.Equal(IrFactSourceKind.Type, Facets(doc, "rating")!.Min!.From.Kind);
        Assert.Equal(IrFactSourceKind.Type, Facets(doc, "rating")!.Max!.From.Kind);
        Assert.NotNull(Facets(doc, "band_plain")!.Values);
        Assert.Null(Facets(doc, "band")!.Values);
        Assert.True(Facets(doc, "code_plain")!.Length!.IsFixed);
        Assert.Equal(new IrLength(6, false, IrLengthUnit.Bytes, Facets(doc, "code")!.Length!.Encoding, Facets(doc, "code")!.Length!.From), Facets(doc, "code")!.Length);
        Assert.NotNull(Facets(doc, "note_plain")!.MinLength);
        Assert.Null(Facets(doc, "note")!.MinLength);
        Assert.Equal(10, Facets(doc, "note")!.Length!.Value);
        Assert.Equal(40, Facets(doc, "email")!.Length!.Value);

        // Each masked column claims exactly what its type does, and is null exactly when its declaration says.
        foreach (var name in new[] { "rating", "band", "code", "note", "email", "seen" })
            Assert.Equal(TypeOnly(Facets(doc, name)), Facets(doc, name));
        Assert.Empty(Field(doc, "rating").NullableBecause);
        Assert.Empty(Field(doc, "code").NullableBecause);
        Assert.NotEmpty(Field(doc, "note").NullableBecause);
        Assert.NotEmpty(Field(doc, "seen").NullableBecause);
    }

    [Fact]
    public void A_mask_added_by_alter_column_masks_the_column_and_leaves_the_table_as_it_is()
    {
        var doc = Procedure(
            "SELECT rating_plain, code_plain FROM clinic.patient;",
            more: ("masks.sql", "ALTER TABLE clinic.patient ALTER COLUMN rating_plain ADD MASKED WITH (FUNCTION = 'default()');"));

        Assert.Empty(doc.Unknowns);
        Assert.Equal(IrFactSourceKind.Type, Facets(doc, "rating_plain")!.Min!.From.Kind);
        Assert.True(Facets(doc, "code_plain")!.Length!.IsFixed);
    }

    [Fact]
    public void A_mask_added_inside_a_body_masks_the_column_since_the_body_may_run()
    {
        var doc = Procedure(
            "SELECT code_plain FROM clinic.patient;",
            more: ("mask_later.sql", "CREATE PROCEDURE clinic.mask_later AS ALTER TABLE clinic.patient ALTER COLUMN code_plain ADD MASKED WITH (FUNCTION = 'default()');"));

        Assert.False(Facets(doc, "code_plain")!.Length!.IsFixed);
    }

    [Fact]
    public void A_mask_dropped_after_the_table_in_its_own_script_is_gone()
    {
        var doc = InferProcedure.Infer(
            "clinic.p",
            ("tables.sql", Tables + "\nALTER TABLE clinic.patient ALTER COLUMN code DROP MASKED;\nGO\n"),
            ("p.sql", "CREATE PROCEDURE clinic.p AS SELECT code FROM clinic.patient;"));

        Assert.Empty(doc.Unknowns);
        Assert.True(Facets(doc, "code")!.Length!.IsFixed);
    }

    [Fact]
    public void A_mask_dropped_in_another_script_or_a_body_stays_since_the_order_is_not_known()
    {
        var elsewhere = Procedure("SELECT code FROM clinic.patient;", more: ("unmask.sql", "ALTER TABLE clinic.patient ALTER COLUMN code DROP MASKED;"));
        var inBody = InferProcedure.Infer(
            "clinic.p",
            ("tables.sql", Tables + "\nIF 1 = 1 ALTER TABLE clinic.patient ALTER COLUMN code DROP MASKED;\nGO\n"),
            ("p.sql", "CREATE PROCEDURE clinic.p AS SELECT code FROM clinic.patient;"));
        var addedAgain = InferProcedure.Infer(
            "clinic.p",
            ("tables.sql", Tables + "\nALTER TABLE clinic.patient ALTER COLUMN code DROP MASKED;\nGO\n"),
            ("again.sql", "ALTER TABLE clinic.patient ALTER COLUMN code ADD MASKED WITH (FUNCTION = 'default()');"),
            ("p.sql", "CREATE PROCEDURE clinic.p AS SELECT code FROM clinic.patient;"));

        Assert.False(Facets(elsewhere, "code")!.Length!.IsFixed);
        Assert.False(Facets(inBody, "code")!.Length!.IsFixed);
        Assert.False(Facets(addedAgain, "code")!.Length!.IsFixed);
    }

    [Fact]
    public void A_session_whose_callers_hold_unmask_claims_everything_of_a_masked_column()
    {
        var body = "SELECT rating, rating_plain, band, band_plain, code, code_plain, note, note_plain FROM clinic.patient;";
        var doc = Procedure(body, new SessionOptions(Unmask: true));

        foreach (var (masked, plain) in new[] { ("rating", "rating_plain"), ("band", "band_plain"), ("code", "code_plain"), ("note", "note_plain") })
            Assert.Equal(Claims(Facets(doc, plain)), Claims(Facets(doc, masked)));
        Assert.Contains(doc.Source.Session, o => o is { Name: "unmask", On: true });

        // Not known and false both leave the mask.
        foreach (var session in new[] { SessionOptions.NotKnown, new SessionOptions(Unmask: false) })
            Assert.Null(Facets(Procedure(body, session), "band")!.Values);
    }

    [Fact]
    public void A_module_that_runs_as_another_user_keeps_every_mask_whatever_the_session_says()
    {
        var owner = ("owner.sql", "CREATE PROCEDURE clinic.as_owner WITH EXECUTE AS OWNER AS SELECT 1 AS one;");
        var caller = ("caller.sql", "CREATE PROCEDURE clinic.as_caller WITH EXECUTE AS CALLER AS SELECT 1 AS one;");
        var kept = InferProcedure.Inferred("clinic.p", new SessionOptions(Unmask: true), ("tables.sql", Tables), owner, ("p.sql", "CREATE PROCEDURE clinic.p AS SELECT band FROM clinic.patient;"));
        var lifted = InferProcedure.Inferred("clinic.p", new SessionOptions(Unmask: true), ("tables.sql", Tables), caller, ("p.sql", "CREATE PROCEDURE clinic.p AS SELECT band FROM clinic.patient;"));

        Assert.Null(Facets(kept.Expected(), "band")!.Values);
        var warning = Assert.Single(kept.Diagnostics);
        Assert.Equal(2103, warning.Code.Number);
        Assert.NotNull(Facets(lifted.Expected(), "band")!.Values);
        Assert.Empty(lifted.Diagnostics);
    }

    /// <summary>The facts of <paramref name="facets"/> with their sources' references left out, which name the column they are of.</summary>
    private static IrFacets? Claims(IrFacets? facets) => facets is null ? null : new IrFacets(
        facets.Length is { } l ? l with { From = Bare(l.From) } : null,
        facets.MinLength is { } m ? m with { From = Bare(m.From) } : null,
        facets.Digits is { } d ? d with { From = Bare(d.From) } : null,
        facets.Min is { } min ? min with { From = Bare(min.From) } : null,
        facets.Max is { } max ? max with { From = Bare(max.From) } : null,
        facets.Values is { } v ? v with { From = [.. v.From.Select(Bare)] } : null);

    private static IrFactSource Bare(IrFactSource source) => source with { Ref = "" };
}
