using Haruspex.Diagnostics;

namespace Haruspex.Tests.Diagnostics;

/// <summary>The one line every diagnostic is written as, for each kind of location and each severity.</summary>
public class DiagnosticFormatterTests
{
    private static readonly DiagnosticCode Broken = new(9001, DiagnosticSeverity.Error, "Something is broken");
    private static readonly DiagnosticCode Doubtful = new(9002, DiagnosticSeverity.Warning, "Something is doubtful");
    private static readonly DiagnosticCode Unknown = new(9003, DiagnosticSeverity.Note, "Something is unknown");

    [Fact]
    public void A_sql_file_is_written_joined_to_the_source_root_with_its_line_and_column() =>
        Assert.Equal(
            "db/procedures/get_order.sql:7:5: error HAR9001: it is broken.",
            DiagnosticFormatter.Format(new Diagnostic(Broken, "it is broken.", new SourceLocation("procedures/get_order.sql", 7, 5)), "db"));

    [Fact]
    public void A_value_in_a_json_document_is_written_with_its_path_before_the_message() =>
        Assert.Equal(
            "haruspex.json:4:14: warning HAR9002: objects: it is doubtful.",
            DiagnosticFormatter.Format(new Diagnostic(Doubtful, "it is doubtful.", new JsonLocation("haruspex.json", "objects", 4, 14))));

    [Fact]
    public void The_root_of_a_json_document_is_written_without_a_path() =>
        Assert.Equal(
            "haruspex.json:1:1: error HAR9001: it is broken.",
            DiagnosticFormatter.Format(new Diagnostic(Broken, "it is broken.", new JsonLocation("haruspex.json", "", 1, 1))));

    [Fact]
    public void A_file_as_a_whole_is_written_without_a_line() =>
        Assert.Equal(
            "haruspex.json: error HAR9001: it is broken.",
            DiagnosticFormatter.Format(new Diagnostic(Broken, "it is broken.", new FileLocation("haruspex.json"))));

    [Fact]
    public void A_diagnostic_without_a_place_is_written_as_the_tool_s_own() =>
        Assert.Equal("haruspex: note HAR9003: it is unknown.", DiagnosticFormatter.Format(new Diagnostic(Unknown, "it is unknown.", null)));

    [Fact]
    public void A_diagnostic_prints_as_the_formatter_writes_it() =>
        Assert.Equal("a.sql:1:2: error HAR9001: it is broken.", new Diagnostic(Broken, "it is broken.", new SourceLocation("a.sql", 1, 2)).ToString());

    [Fact]
    public void A_code_is_written_with_four_digits_and_takes_its_severity_to_the_diagnostic()
    {
        var diagnostic = new Diagnostic(new DiagnosticCode(1001, DiagnosticSeverity.Warning, "A title"), "m", null);

        Assert.Equal("HAR1001", diagnostic.Code.ToString());
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.False(diagnostic.IsError);
    }

    [Fact]
    public void A_code_outside_four_digits_or_without_a_title_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticCode(999, DiagnosticSeverity.Error, "A title"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticCode(10000, DiagnosticSeverity.Error, "A title"));
        Assert.Throws<ArgumentException>(() => new DiagnosticCode(1000, DiagnosticSeverity.Error, " "));
    }

    [Fact]
    public void Diagnostics_and_locations_compare_by_value() =>
        EqualityLaws.Hold([
            new Diagnostic(Broken, "m", new JsonLocation("f", "p", 1, 2)),
            new Diagnostic(new DiagnosticCode(9001, DiagnosticSeverity.Error, "Something is broken"), "m", new JsonLocation("f", "p", 1, 2)),
            new Diagnostic(Broken, "m", new SourceLocation("f", 1, 2)),
            new Diagnostic(Broken, "m", new FileLocation("f")),
            new Diagnostic(Broken, "m", null),
        ]);
}
