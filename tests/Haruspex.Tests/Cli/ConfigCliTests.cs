using Haruspex.Cli;
using Haruspex.Diagnostics;

namespace Haruspex.Tests.Cli;

/// <summary>
/// <c>haruspex generate</c> with a config file, end to end on the shop fixture (<c>tests/fixtures/shop</c>), whose
/// <c>haruspex.json</c> uses selection, overrides, strict objects and naming. Errors go to standard error with the file,
/// the path inside it and the line.
/// </summary>
public class ConfigCliTests
{
    private static string Shop => Fixture.Named("shop").Folder;

    private static (int ExitCode, string Output, string Error) Run(string workingDirectory, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(args, output, error, workingDirectory);
        return (exitCode, output.ToString(), error.ToString().ReplaceLineEndings("\n"));
    }

    /// <summary>A copy of the shop fixture with its config replaced, to try a broken one.</summary>
    private static TempDirectory ShopWith(string config)
    {
        var copy = new TempDirectory();
        foreach (var file in Directory.GetFiles(Path.Combine(Shop, "database"), "*.sql", SearchOption.AllDirectories))
            copy.File(Path.GetRelativePath(Shop, file), File.ReadAllText(file));
        copy.File("haruspex.json", config);
        return copy;
    }

    [Fact]
    public void A_config_file_that_does_not_read_is_reported_alone_without_the_settings_it_would_have_given()
    {
        using var dir = new TempDirectory();
        dir.File("haruspex.json", "{ \"source\": ");

        var (exitCode, _, error) = Run(dir.Path, "generate");

        Assert.Equal(2, exitCode);
        var line = Assert.Single(error.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'));
        Assert.Contains("error HAR3002:", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_config_file_that_does_not_read_and_a_malformed_include_are_both_reported()
    {
        using var dir = new TempDirectory();
        dir.File("haruspex.json", "{ \"source\": ");

        var (exitCode, _, error) = Run(dir.Path, "generate", "--include", "a.b.c");

        Assert.Equal(2, exitCode);
        var lines = error.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("error HAR3002:", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("haruspex: error HAR6006: ", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_config_file_and_a_malformed_include_are_both_reported()
    {
        using var dir = new TempDirectory();

        var (exitCode, _, error) = Run(dir.Path, "generate", "--config", "nowhere.json", "--include", "a.b.c");

        Assert.Equal(2, exitCode);
        Assert.Equal(
            "haruspex: error HAR6002: config file not found: nowhere.json\n"
            + "haruspex: error HAR6006: invalid --include pattern: 'a.b.c' is not a two-part name such as sales.get_order, sales.* or *.list_*.\n",
            error.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Generates_the_shop_from_its_config_file()
    {
        using var outDir = new TempDirectory();

        var (exitCode, output, error) = Run(Shop, "generate", "--out", outDir.Path);

        Assert.Equal("", error);
        Assert.Equal(0, exitCode);
        Assert.Contains("haruspex: 4 procedures, 0 unknown(s).", output, StringComparison.Ordinal);

        // What it writes is the shop's expected output, which FixtureSnapshotTests checks for every project fixture.
        Assert.True(File.Exists(Path.Combine(outDir.Path, "haruspex.ir.json")));
        Assert.True(File.Exists(Path.Combine(outDir.Path, "haruspex.zod.ts")));
    }

    private const string Dynamic = """
        CREATE SCHEMA rpt;
        GO
        CREATE PROCEDURE rpt.search @where nvarchar(200) AS
        DECLARE @sql nvarchar(max) = N'SELECT 1 AS n WHERE ' + @where;
        EXEC (@sql);
        GO
        """;

    [Fact]
    public void An_undeclared_unknowable_exec_is_a_warning_naming_it_and_generation_goes_ahead()
    {
        // A planned check command will fail it; generate warns, and its exit codes are as before.
        using var dir = new TempDirectory();
        dir.File("database/search.sql", Dynamic);
        dir.File("haruspex.json", """{ "source": "database", "out": "out", "schemas": { "rpt": { "include": ["search"] } } }""");

        var (exitCode, output, error) = Run(dir.Path, "generate");

        Assert.Equal(0, exitCode);
        Assert.Equal(
            $"{DiagnosticFormatter.PathAsShown(Path.Combine(dir.Path, "haruspex.json"))}: warning HAR4204: rpt.search: its result sets are unknown: EXEC (@sql) runs dynamic SQL, a string built when the procedure runs, so its result sets are not known. (search.sql:5:1) Declare them in haruspex.json under procedures[\"rpt.search\"].results, as \"none\" or a JSON Schema, or declare the results of the procedure that runs it.\n",
            error);
        Assert.Contains("haruspex: 1 procedure, 1 unknown(s).", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declaration_types_an_unknowable_procedure()
    {
        using var dir = new TempDirectory();
        dir.File("database/search.sql", Dynamic);
        dir.File("haruspex.json", """
            {
              "source": "database", "out": "out",
              "schemas": { "rpt": { "include": ["search"] } },
              "procedures": { "rpt.search": { "results": { "rowset": { "type": "object", "properties": { "n": { "type": "integer", "format": "int32" } } } }, "why": "the query is built from @where" } }
            }
            """);

        var (exitCode, output, error) = Run(dir.Path, "generate");

        Assert.Equal("", error);
        Assert.Equal(0, exitCode);
        Assert.Contains("haruspex: 1 procedure, 0 unknown(s).", output, StringComparison.Ordinal);
        Assert.Contains("n: z.int32(),", File.ReadAllText(Path.Combine(dir.Path, "out", "haruspex.zod.ts")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_shop_s_config_shows_in_its_output()
    {
        using var outDir = new TempDirectory();
        Run(Shop, "generate", "--out", outDir.Path);
        var zod = File.ReadAllText(Path.Combine(outDir.Path, "haruspex.zod.ts"));

        // Naming: no schema in names, # → Row, shop_admin's prefix and own rule, and the overrides.
        Assert.Contains("export const BasketSchema = ", zod, StringComparison.Ordinal);
        Assert.Contains("export const BasketItemSchema = ", zod, StringComparison.Ordinal);
        Assert.Contains("export const BasketLineRowSchema = ", zod, StringComparison.Ordinal);
        Assert.Contains("export const ListProductsSchema = ", zod, StringComparison.Ordinal);
        Assert.Contains("export const AdminSalesSchema = ", zod, StringComparison.Ordinal);

        // Selection: the excluded and the unselected procedures are not typed.
        Assert.DoesNotContain("Recalculate", zod, StringComparison.Ordinal);
        Assert.DoesNotContain("Purge", zod, StringComparison.Ordinal);

        // Strict objects, nullable: false, exact values and the import.
        Assert.DoesNotContain("z.object(", zod, StringComparison.Ordinal);
        Assert.Contains("  customerName: z.string().max(60),\n", zod, StringComparison.Ordinal);
        Assert.Contains("  status: z.enum([\"active\", \"retired\"]),\n", zod, StringComparison.Ordinal);
        Assert.Contains("import { ProductAttributesSchema } from '@shop/types'\n", zod, StringComparison.Ordinal);
    }

    [Fact]
    public void The_output_is_the_same_on_every_run()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();

        Run(Shop, "generate", "--out", first.Path);
        Run(Shop, "generate", "--out", second.Path);

        foreach (var file in new[] { "haruspex.ir.json", "haruspex.zod.ts" })
        {
            var bytes = File.ReadAllBytes(Path.Combine(first.Path, file));
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(second.Path, file)));
            Assert.DoesNotContain((byte)'\r', bytes);
        }
    }

    [Fact]
    public void The_config_file_is_found_from_a_folder_below_it()
    {
        using var outDir = new TempDirectory();

        var (exitCode, output, _) = Run(Path.Combine(Shop, "database", "procedures"), "generate", "--out", outDir.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains("4 procedures", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_names_the_file_from_anywhere()
    {
        using var cwd = new TempDirectory();
        using var outDir = new TempDirectory();

        var (exitCode, output, _) = Run(cwd.Path, "generate", "--config", Path.Combine(Shop, "haruspex.json"), "--out", outDir.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains("4 procedures", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_config_file_is_a_usage_error()
    {
        using var cwd = new TempDirectory();

        var (exitCode, _, error) = Run(cwd.Path, "generate", "--config", "nope.json");

        Assert.Equal(2, exitCode);
        Assert.Equal("haruspex: error HAR6002: config file not found: nope.json\n", error);
    }

    [Fact]
    public void Out_on_the_command_line_wins_and_the_file_s_out_is_relative_to_it()
    {
        using var copy = ShopWith("""{ "source": "database", "out": "build/zod", "schemas": { "shop": { "include": ["list_products"] } } }""");

        var (exitCode, output, _) = Run(Path.Combine(copy.Path, "database"), "generate");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(copy.Path, "build", "zod", "haruspex.zod.ts")), output);
    }

    [Fact]
    public void The_folder_argument_overrides_source()
    {
        using var copy = ShopWith("""{ "source": "nowhere", "schemas": { "shop": { "include": ["list_products"] } } }""");
        using var outDir = new TempDirectory();

        var (exitCode, _, _) = Run(copy.Path, "generate", Path.Combine(copy.Path, "database"), "--out", outDir.Path);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Include_on_the_command_line_is_a_one_off_override()
    {
        using var outDir = new TempDirectory();

        var (exitCode, output, error) = Run(Shop, "generate", "--include", "shop.list_products", "--out", outDir.Path);

        // The file's overrides of procedures this run leaves out are skipped, and its excludes are not checked against it.
        Assert.Equal("", error);
        Assert.Equal(0, exitCode);
        Assert.Contains("1 procedure,", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_file_s_exclude_still_applies_to_a_command_line_include()
    {
        using var outDir = new TempDirectory();

        var (exitCode, output, _) = Run(Shop, "generate", "--include", "shop.*", "--out", outDir.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains("3 procedures", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_config_error_names_the_file_the_path_and_the_line()
    {
        using var copy = ShopWith("{\n  \"schemas\": { \"shop\": { \"include\": [\"*\"] } },\n  \"objects\": \"loose\"\n}\n");

        var (exitCode, output, error) = Run(copy.Path, "generate", "--out", Path.Combine(copy.Path, "out"));

        Assert.Equal(2, exitCode);
        Assert.Equal("", output);
        Assert.Equal($"{DiagnosticFormatter.PathAsShown(Path.Combine(copy.Path, "haruspex.json"))}:3:14: error HAR3005: objects: must be \"strip\" or \"strict\", not \"loose\".\n", error);
        Assert.False(Directory.Exists(Path.Combine(copy.Path, "out")));
    }

    [Fact]
    public void An_include_in_the_file_that_matches_nothing_is_an_error_with_its_line()
    {
        using var copy = ShopWith("{\n  \"source\": \"database\",\n  \"schemas\": { \"shop\": { \"include\": [\"*\",\n    \"get_baskte\"] } }\n}\n");

        var (exitCode, _, error) = Run(copy.Path, "generate", "--out", Path.Combine(copy.Path, "out"));

        Assert.Equal(2, exitCode);
        Assert.Equal($"{DiagnosticFormatter.PathAsShown(Path.Combine(copy.Path, "haruspex.json"))}:4:5: error HAR4002: schemas.shop.include[1]: 'get_baskte' matches no procedure in the schema \"shop\". Correct it, or remove it.\n", error);
    }

    [Fact]
    public void An_exclude_that_excludes_nothing_is_an_error_with_its_line()
    {
        using var copy = ShopWith("{ \"source\": \"database\", \"schemas\": { \"shop\": { \"include\": [\"*\"], \"exclude\": [\"*_old\"] } } }");

        var (exitCode, _, error) = Run(copy.Path, "generate", "--out", Path.Combine(copy.Path, "out"));

        Assert.Equal(2, exitCode);
        Assert.Equal($"{DiagnosticFormatter.PathAsShown(Path.Combine(copy.Path, "haruspex.json"))}:1:78: error HAR4003: schemas.shop.exclude[0]: '*_old' excludes nothing: it matches no procedure that \"include\" selects in the schema \"shop\". Remove it.\n", error);
    }

    [Fact]
    public void No_include_anywhere_selects_nothing_and_says_so()
    {
        using var copy = ShopWith("{ \"source\": \"database\" }");

        var (exitCode, _, error) = Run(copy.Path, "generate", "--out", Path.Combine(copy.Path, "out"));

        Assert.Equal(2, exitCode);
        Assert.Equal($"haruspex: error HAR6007: nothing is selected: give --include, or a schema with \"include\" in {DiagnosticFormatter.PathAsShown(Path.Combine(copy.Path, "haruspex.json"))}, such as \"schemas\": {{ \"dbo\": {{ \"include\": [\"*\"] }} }}.\n", error);
    }

    // TypeScript's reserved words are the emitter's knowledge, which the CLI hands to the config (ReservedWords), so a reserved
    // name is reported where the config file gives it, not later by the emitter without a line.
    [Fact]
    public void A_reserved_word_given_as_a_name_is_an_error_at_its_line()
    {
        using var copy = ShopWith("""
            { "source": "database", "schemas": { "shop": { "include": ["list_products"] } },
              "procedures": { "shop.list_products": { "name": "delete" } } }
            """);

        var (exitCode, _, error) = Run(copy.Path, "generate", "--out", Path.Combine(copy.Path, "out"));

        Assert.Equal(2, exitCode);
        Assert.StartsWith($"{DiagnosticFormatter.PathAsShown(Path.Combine(copy.Path, "haruspex.json"))}:2:", error, StringComparison.Ordinal);
        Assert.Contains("error HAR3019: procedures[\"shop.list_products\"].name: 'delete' is not a valid name", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(copy.Path, "out")));
    }

    [Fact]
    public void Naming_rules_that_give_a_reserved_word_are_an_error_naming_typescript()
    {
        using var copy = ShopWith("""
            { "source": "database",
              "schemas": { "shop": { "include": ["list_products"], "naming": { "case": "camel", "replace": [{ "pattern": "^list_products$", "with": "delete" }] } } } }
            """);

        var (exitCode, _, error) = Run(copy.Path, "generate", "--out", Path.Combine(copy.Path, "out"));

        Assert.Equal(2, exitCode);
        Assert.Contains("the rules give 'delete', which is a reserved word in TypeScript.", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(copy.Path, "out")));
    }

    [Fact]
    public void Clashing_names_are_an_error_and_nothing_is_written()
    {
        // Without its prefix, shop_admin's report would be Sales; a procedure named Sales by override then clashes.
        using var copy = ShopWith("""
            { "source": "database",
              "schemas": { "shop": { "include": ["list_products"] }, "shop_admin": { "include": ["*"], "exclude": ["purge_*"], "naming": { "replace": [{ "pattern": "^report_", "with": "" }] } } },
              "procedures": { "shop.list_products": { "name": "Sales" } } }
            """);

        var (exitCode, _, error) = Run(copy.Path, "generate", "--out", Path.Combine(copy.Path, "out"));

        Assert.Equal(2, exitCode);
        Assert.StartsWith(
            $"{DiagnosticFormatter.PathAsShown(Path.Combine(copy.Path, "haruspex.json"))}:2:58: error HAR4303: schemas.shop_admin: \"Sales\" would name both the procedure shop.list_products and the procedure shop_admin.report_sales. Generated names must not clash: give one of their schemas a \"prefix\"",
            error,
            StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(copy.Path, "out")));
    }

    [Fact]
    public void A_schema_whose_exclude_removes_everything_it_includes_is_an_error()
    {
        using var copy = ShopWith("""{ "source": "database", "schemas": { "shop": { "include": ["recalculate_*"], "exclude": ["*_internal"] } } }""");

        var (exitCode, output, error) = Run(copy.Path, "generate", "--out", Path.Combine(copy.Path, "out"));

        Assert.Equal(2, exitCode);
        Assert.Equal("", output);
        Assert.Equal(
            $"{DiagnosticFormatter.PathAsShown(Path.Combine(copy.Path, "haruspex.json"))}:1:38: error HAR4004: schemas.shop: the schema \"shop\" selects nothing: its \"exclude\" removes every procedure its \"include\" selects. Narrow the exclude, or remove the block.\n",
            error);
        Assert.False(Directory.Exists(Path.Combine(copy.Path, "out")));
    }

    [Fact]
    public void A_command_line_include_whose_matches_the_file_excludes_is_an_error()
    {
        using var outDir = new TempDirectory();

        var (exitCode, output, error) = Run(Shop, "generate", "--include", "shop.recalculate_internal", "--out", outDir.Path);

        Assert.Equal(2, exitCode);
        Assert.Equal("", output);
        Assert.Equal("haruspex: error HAR4006: --include 'shop.recalculate_internal' matches only procedures that schemas.shop.exclude leaves out, so it selects nothing.\n", error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outDir.Path));
    }

    [Fact]
    public void A_schema_that_does_not_exist_is_an_error()
    {
        using var copy = ShopWith("""{ "source": "database", "schemas": { "shop": { "include": ["*"] }, "shopp": { "include": ["*"] } } }""");

        var (exitCode, _, error) = Run(copy.Path, "generate", "--out", Path.Combine(copy.Path, "out"));

        Assert.Equal(2, exitCode);
        Assert.Contains("error HAR4001: schemas.shopp: the project has no procedure in a schema named \"shopp\".", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_warning_is_written_and_generation_goes_ahead()
    {
        using var copy = ShopWith("""
            { "source": "database", "schemas": { "shop": { "include": ["list_products"] } },
              "procedures": { "shop.list_products": { "paths": { "[].sku": { "nullable": false, "why": "a key" } } } } }
            """);

        var (exitCode, _, error) = Run(copy.Path, "generate", "--out", Path.Combine(copy.Path, "out"));

        Assert.Equal(0, exitCode);
        Assert.Equal(
            $"{DiagnosticFormatter.PathAsShown(Path.Combine(copy.Path, "haruspex.json"))}:2:54: warning HAR4110: procedures[\"shop.list_products\"].paths[\"[].sku\"]: \"nullable\": false agrees with the inference, which already proves the value at results[0][].sku is never null. Remove it.\n",
            error);
    }

    [Fact]
    public void An_import_that_clashes_with_a_generated_name_is_an_error()
    {
        using var copy = ShopWith("""
            { "source": "database", "schemas": { "shop": { "include": ["list_products"] } },
              "procedures": { "shop.list_products": { "paths": { "[].attributes": { "type": "ListProductsRowSchema", "import": "./types" } } } } }
            """);

        var (exitCode, _, error) = Run(copy.Path, "generate", "--out", Path.Combine(copy.Path, "out"));

        Assert.Equal(2, exitCode);
        Assert.Equal($"{DiagnosticFormatter.PathAsShown(Path.Combine(copy.Path, "haruspex.json"))}: error HAR5004: The imported name 'ListProductsRowSchema' is also a name this module defines or uses.\n", error);
    }
}
