using System.Text.RegularExpressions;
using Haruspex.Collections;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>
/// The Zod module split into a module per group of objects, with what two or more modules share in a shared module
/// (docs/decisions/0074-a-config-may-split-its-output-into-modules.md and
/// docs/decisions/0075-what-several-modules-share-is-written-once-in-a-shared-file.md).
/// </summary>
public partial class ZodModulesTests
{
    private const string Schemas = "CREATE SCHEMA sales;\nGO\nCREATE SCHEMA shop;\nGO\nCREATE SCHEMA admin;";

    // get_order and get_product return one shape, across two modules; list_orders and find_order another, within sales;
    // list_users a third, in admin alone.
    private static readonly (string File, string Sql)[] Procedures =
    [
        ("schemas.sql", Schemas),
        ("get_order.sql", "CREATE PROCEDURE sales.get_order AS SELECT CAST(1 AS int) AS order_id, CAST(2.5 AS decimal(9,2)) AS total;"),
        ("get_product.sql", "CREATE PROCEDURE shop.get_product AS SELECT CAST(1 AS int) AS order_id, CAST(2.5 AS decimal(9,2)) AS total;"),
        ("list_orders.sql", "CREATE PROCEDURE sales.list_orders AS SELECT CAST(N'a' AS nvarchar(10)) AS note;"),
        ("find_order.sql", "CREATE PROCEDURE sales.find_order AS SELECT CAST(N'a' AS nvarchar(10)) AS note;"),
        ("list_users.sql", "CREATE PROCEDURE admin.list_users AS SELECT CAST(N'a' AS nvarchar(20)) AS name;"),
    ];

    private static IrModules BySchema(IrDocument document) =>
        IrModules.Of(document, document.Objects.ToDictionary(o => o.Id, o => o.Id[..o.Id.IndexOf('.', StringComparison.Ordinal)], StringComparer.Ordinal));

    private static EquatableMap<string, string> Split(IrDocument document) => ZodEmitter.Emit(document, new ZodOptions(), BySchema(document)).Expected();

    [GeneratedRegex(@"^export (?:const|type) (\w+)", RegexOptions.Multiline)]
    private static partial Regex Export();

    [GeneratedRegex(@"\bshape[0-9a-f]{6,}\b")]
    private static partial Regex ShapeName();

    private static List<string> Exports(string module) => [.. Export().Matches(module).Select(m => m.Groups[1].Value)];

    [Fact]
    public void Each_module_is_a_file_and_what_two_share_is_in_the_shared_one()
    {
        var modules = Split(InferProcedure.Infer("*.*", Procedures));

        Assert.Equal(["admin", "sales", "shared", "shop"], modules.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_name_the_unsplit_module_exports_is_exported_once_by_one_of_the_modules()
    {
        var document = InferProcedure.Infer("*.*", Procedures);
        var whole = ZodEmitter.Emit(document).Expected();

        var split = Split(document).Values.SelectMany(Exports).Where(name => !ShapeName().IsMatch(name)).ToList();

        Assert.Equal(Exports(whole).Order(StringComparer.Ordinal), split.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_shape_two_modules_share_is_exported_from_the_shared_module_as_internal_under_its_unsplit_name()
    {
        var document = InferProcedure.Infer("*.*", Procedures);
        var modules = Split(document);
        var shared = modules[IrModules.Shared];
        var shape = ShapeName().Match(shared).Value;

        Assert.Contains($" * @internal\n */\nexport const {shape} = z.object(", shared, StringComparison.Ordinal);
        Assert.DoesNotContain("export type " + shape, shared, StringComparison.Ordinal);
        Assert.Contains($"const {shape} = ", ZodEmitter.Emit(document).Expected(), StringComparison.Ordinal);
        Assert.Contains($"import {{ {shape} }} from './shared.zod.js'\n", modules["sales"], StringComparison.Ordinal);
        Assert.Contains($"export const GetProductSchema = z.array({shape})", modules["shop"].Replace("GetProductRowSchema", shape, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void A_shape_one_module_shares_stays_in_it_unexported_and_a_module_sharing_nothing_imports_zod_alone()
    {
        var modules = Split(InferProcedure.Infer("*.*", Procedures));
        var sales = modules["sales"];
        var local = ShapeName().Matches(sales).Select(m => m.Value).Distinct().Single(s => !modules[IrModules.Shared].Contains(s, StringComparison.Ordinal));

        Assert.Contains($"\nconst {local} = z.object(", sales, StringComparison.Ordinal);
        Assert.DoesNotContain(local, modules[IrModules.Shared], StringComparison.Ordinal);
        Assert.Equal(["import { z } from 'zod'"], modules["admin"].Split('\n').Where(l => l.StartsWith("import ", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_module_never_imports_another_and_the_shared_one_imports_nothing_of_theirs()
    {
        var modules = Split(InferProcedure.Infer("*.*", Procedures));

        foreach (var (name, text) in modules)
        {
            var from = text.Split('\n').Where(l => l.StartsWith("import ", StringComparison.Ordinal)).Select(l => l[(l.LastIndexOf(" from ", StringComparison.Ordinal) + 6)..]).ToList();
            string[] allowed = name == IrModules.Shared ? ["'zod'"] : ["'zod'", "'./shared.zod.js'"];
            Assert.All(from, f => Assert.Contains(f, allowed));
        }
    }

    [Fact]
    public void A_procedure_added_to_one_schema_changes_only_its_module()
    {
        var before = Split(InferProcedure.Infer("*.*", Procedures));

        var after = Split(InferProcedure.Infer("*.*", [.. Procedures, ("get_total.sql", "CREATE PROCEDURE sales.get_total AS SELECT CAST(1 AS bigint) AS n;")]));

        Assert.Equal(["sales"], before.Keys.Where(k => before[k] != after[k]));
    }

    [Fact]
    public void A_procedure_added_with_a_shape_another_module_has_changes_its_module_and_the_shared_one()
    {
        var before = Split(InferProcedure.Infer("*.*", Procedures));

        var after = Split(InferProcedure.Infer("*.*", [.. Procedures, ("get_user.sql", "CREATE PROCEDURE shop.get_user AS SELECT CAST(N'a' AS nvarchar(20)) AS name;")]));

        // admin.list_users' rows are now a shape two modules share: declared in the shared module, which admin imports.
        Assert.Equal(["admin", "shared", "shop"], before.Keys.Where(k => before[k] != after[k]).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_shared_module_says_what_it_is_in_its_header()
    {
        var shared = Split(InferProcedure.Infer("*.*", Procedures))[IrModules.Shared];

        Assert.Contains("// The definitions two or more modules of this config share", shared, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_shared_writes_no_shared_module()
    {
        var modules = Split(InferProcedure.Infer("*.*", [Procedures[0], Procedures[1], Procedures[5]]));

        Assert.Equal(["admin", "sales"], modules.Keys.Order(StringComparer.Ordinal));
    }
}
