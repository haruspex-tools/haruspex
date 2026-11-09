using System.Text;
using Haruspex.Cli;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>
/// The config file applied to inferred IR: overrides as transformations of the document, then naming. Each error and
/// warning is checked word for word, since the message is how a user finds what to change.
/// </summary>
public class ConfigApplicationTests
{
    private const string Tables = """
        CREATE SCHEMA sales;
        GO
        CREATE TABLE sales.customer
        (
            customer_id int NOT NULL PRIMARY KEY,
            name nvarchar(40) NULL,
            status varchar(8) COLLATE Latin1_General_100_CI_AS NOT NULL CHECK (status IN ('open', 'closed')),
            level int NOT NULL CHECK (level IN (1, 2)),
            notes json NULL
        );
        GO
        CREATE TABLE sales.line (line_id int NOT NULL PRIMARY KEY, customer_id int NOT NULL, sku varchar(10) NOT NULL);
        GO
        """;

    /// <summary>Customers as JSON: a nullable name, a collation set, an exact set, JSON text and nested lines.</summary>
    private const string GetCustomer = """
        CREATE PROCEDURE sales.get_customer AS
        SELECT c.customer_id AS id, c.name, c.status, c.level, c.notes,
               (SELECT l.sku FROM sales.line AS l WHERE l.customer_id = c.customer_id FOR JSON PATH) AS lines
        FROM sales.customer AS c
        FOR JSON PATH;
        GO
        """ + "\n";

    private sealed record Run(ConfigApplied Applied)
    {
        public IrDocument Document => Applied.Document ?? throw new InvalidOperationException(string.Join("\n", Applied.Problems));

        public IReadOnlyList<string> Problems => [.. Applied.Problems.Select(p => p.ToString())];

        public string Error => Assert.Single(Applied.Problems, p => p.Severity == ConfigSeverity.Error).ToString();
    }

    private static Run Apply(string procedures, string config, string include = "*.*", string? output = null, IReadOnlyCollection<string>? selectedByFile = null)
    {
        using var dir = new TempDirectory();
        dir.File("tables.sql", Tables);
        dir.File("procedures.sql", procedures);
        var project = SqlProject.Load(dir.Path);
        Assert.Empty(project.Diagnostics);

        var read = ConfigReader.Read(Encoding.UTF8.GetBytes(config), "haruspex.json", dir.Path);
        Assert.Empty(read.Problems);
        var (selected, _) = ProcedureSelection.Select(project.Procedures, [ObjectPattern.Parse(include)], read.Config!);
        var byFile = selectedByFile?.Select(t => project.Procedures.Single(p => p.ToString() == t)).ToList();
        return new Run(project.Infer(selected, read.Config!, byFile, output is null ? null : Path.Combine(dir.Path, output)));
    }

    private static IrObjectType Type(IrDocument doc, string name) => doc.Types[name];

    private static IrField Field(IrDocument doc, string type, string name) => doc.Types[type].Fields.Single(f => f.Name == name);

    // Naming.

    [Fact]
    public void Default_names_leave_the_schema_out_and_build_nested_names_on_the_procedure()
    {
        var doc = Apply(GetCustomer, "{}").Document;

        var procedure = Assert.Single(doc.Objects);
        Assert.Equal(("sales.get_customer", "GetCustomer"), (procedure.Id, procedure.Name));
        Assert.Equal(["GetCustomerLine", "GetCustomerRow"], doc.Types.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new IrTypeIdentity(IrTypeOwner.Procedure, "sales.get_customer", "results[0][]"), Type(doc, "GetCustomerRow").Identity);
        Assert.Equal(new IrTypeIdentity(IrTypeOwner.Procedure, "sales.get_customer", "results[0][].lines[]"), Type(doc, "GetCustomerLine").Identity);
    }

    [Fact]
    public void Naming_rules_apply_to_the_procedure_and_its_types()
    {
        var doc = Apply(GetCustomer, """{ "naming": { "case": "camel" } }""").Document;

        Assert.Equal("getCustomer", doc.Objects[0].Name);
        Assert.Equal(["getCustomerLine", "getCustomerRow"], doc.Types.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_schema_s_prefix_and_naming_apply_to_its_procedures_and_their_types()
    {
        var doc = Apply(GetCustomer, """
            { "naming": { "replace": [{ "pattern": "^get_", "with": "" }] },
              "schemas": { "sales": { "prefix": "Sales", "naming": { "case": "camel", "replace": [{ "pattern": "customer", "with": "client" }] } } } }
            """).Document;

        Assert.Equal("salesClient", doc.Objects[0].Name);
        Assert.Equal(["salesClientLine", "salesClientRow"], doc.Types.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_name_override_names_the_procedure_and_the_types_built_on_it()
    {
        var doc = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "name": "Customers" } } }""").Document;

        Assert.Equal("Customers", doc.Objects[0].Name);
        Assert.Equal(["CustomersLine", "CustomersRow"], doc.Types.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_path_name_override_names_a_nested_type()
    {
        var doc = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].lines[]": { "name": "CustomerLine" }, "[]": { "name": "Customer" } } } } }""").Document;

        Assert.Equal(["Customer", "CustomerLine"], doc.Types.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_name_override_the_rules_already_give_is_a_warning()
    {
        var run = Apply(GetCustomer, """
            { "procedures": { "sales.get_customer": { "name": "GetCustomer",
              "paths": { "[].lines[]": { "name": "GetCustomerLine" } } } } }
            """);

        Assert.Equal(
            [
                "haruspex.json:1:51: warning: procedures[\"sales.get_customer\"].name: the name \"GetCustomer\" is what the naming rules give sales.get_customer already. Remove it.",
                "haruspex.json:2:14: warning: procedures[\"sales.get_customer\"].paths[\"[].lines[]\"]: the name \"GetCustomerLine\" is what the naming rules give the type at results[0][].lines[] already. Remove it.",
            ],
            run.Problems);
        Assert.NotNull(run.Applied.Document);
    }

    [Fact]
    public void Two_procedures_whose_names_clash_are_an_error_naming_both()
    {
        var run = Apply("CREATE PROCEDURE dbo.person AS SELECT 1 AS a;\nGO\nCREATE PROCEDURE [dbo].[person#] AS SELECT 2 AS a;\nGO\n", "{}");

        Assert.Null(run.Applied.Document);
        Assert.Equal(
            "haruspex.json: error: \"Person\" would name both the procedure dbo.person and the procedure dbo.person#. Generated names must not clash: give one of their schemas a \"prefix\" in \"schemas\", or one of them its own name with an override in \"procedures\" (\"name\" for a procedure, \"paths\": { \"<path>\": { \"name\": … } } for a type in its results).",
            run.Error);
    }

    [Fact]
    public void A_replacement_rule_resolves_a_clash_from_the_name_itself()
    {
        var doc = Apply("CREATE PROCEDURE dbo.person AS SELECT 1 AS a;\nGO\nCREATE PROCEDURE [dbo].[person#] AS SELECT 2 AS a;\nGO\n",
            """{ "naming": { "replace": [{ "pattern": "#$", "with": "Entry" }] } }""").Document;

        Assert.Equal(["Person", "PersonEntry"], doc.Objects.Select(o => o.Name));
    }

    [Fact]
    public void Procedures_of_two_schemas_whose_names_clash_are_an_error_and_a_prefix_resolves_it()
    {
        const string Two = "CREATE PROCEDURE sales.get_customer2 AS SELECT 1 AS a;\nGO\nCREATE SCHEMA audit;\nGO\nCREATE PROCEDURE audit.get_customer2 AS SELECT 2 AS a;\nGO\n";

        Assert.StartsWith("haruspex.json: error: \"GetCustomer2\" would name both the procedure audit.get_customer2 and the procedure sales.get_customer2.", Apply(Two, "{}").Error, StringComparison.Ordinal);
        Assert.Equal(["AuditGetCustomer2", "GetCustomer2"], Apply(Two, """{ "schemas": { "audit": { "prefix": "Audit" } } }""").Document.Objects.Select(o => o.Name));
    }

    [Fact]
    public void A_nested_type_that_clashes_with_another_procedure_is_an_error_naming_both()
    {
        var run = Apply(GetCustomer + "CREATE PROCEDURE sales.get_customer_line AS SELECT 1 AS a;\nGO\n", "{}");

        Assert.StartsWith(
            "haruspex.json: error: \"GetCustomerLine\" would name both the procedure sales.get_customer_line and the type at results[0][].lines[] in sales.get_customer.",
            run.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_clash_is_resolved_by_an_override()
    {
        var doc = Apply(GetCustomer + "CREATE PROCEDURE sales.get_customer_line AS SELECT 1 AS a;\nGO\n",
            """{ "procedures": { "sales.get_customer_line": { "name": "CustomerLine" } } }""").Document;

        Assert.Equal(["GetCustomer", "CustomerLine"], doc.Objects.Select(o => o.Name));
        Assert.Contains("GetCustomerLine", doc.Types.Keys);
    }

    [Fact]
    public void Adding_a_procedure_never_renames_another()
    {
        var alone = Apply(GetCustomer, "{}").Document;
        var withAnother = Apply(GetCustomer + "CREATE PROCEDURE sales.get_line AS SELECT 1 AS a;\nGO\n", "{}").Document;

        Assert.Equal(alone.Types.Keys.Order(StringComparer.Ordinal), withAnother.Types.Keys.Where(k => k.StartsWith("GetCustomer", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    /// <summary>Within one procedure a number still tells two types apart: it depends only on that procedure's shape.</summary>
    [Fact]
    public void Two_types_of_one_procedure_with_one_name_are_numbered_within_it()
    {
        var doc = Apply("CREATE PROCEDURE dbo.p AS SELECT c.customer_id AS [row.id] FROM sales.customer AS c FOR JSON PATH;\nGO\n", "{}").Document;

        // The rows are PRow; the nested object "row" would be P + Row too.
        Assert.Equal(["PRow", "PRow2"], doc.Types.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("PRow2", Assert.IsType<IrRef>(Field(doc, "PRow", "row").Type).Name);
    }

    [Fact]
    public void A_name_override_that_clashes_is_an_error()
    {
        var run = Apply(GetCustomer + "CREATE PROCEDURE sales.other AS SELECT 1 AS a;\nGO\n",
            """{ "procedures": { "sales.other": { "name": "GetCustomerRow" } } }""");

        Assert.StartsWith("haruspex.json: error: \"GetCustomerRow\" would name both the procedure sales.other and the type at results[0][] in sales.get_customer.", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_result_sets_are_numbered_and_a_path_names_one()
    {
        var doc = Apply("CREATE PROCEDURE dbo.p AS SELECT 1 AS a;\nSELECT c.customer_id AS id FROM sales.customer AS c;\nGO\n",
            """{ "procedures": { "dbo.p": { "paths": { "results[1][]": { "name": "Customer" } } } } }""").Document;

        Assert.Equal(["Customer", "PResult1Row"], doc.Types.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_path_without_its_result_set_is_an_error_when_there_are_several()
    {
        var run = Apply("CREATE PROCEDURE dbo.p AS SELECT 1 AS a;\nSELECT 2 AS b;\nGO\n",
            """{ "procedures": { "dbo.p": { "paths": { "[]": { "name": "X" } } } } }""");

        Assert.Equal("haruspex.json:1:41: error: procedures[\"dbo.p\"].paths[\"[]\"]: dbo.p returns 2 result sets, so the path must start with the one it means, as results[0].", run.Error);
    }

    [Fact]
    public void A_result_set_that_does_not_exist_is_an_error()
    {
        var run = Apply("CREATE PROCEDURE dbo.p AS SELECT 1 AS a;\nGO\n", """{ "procedures": { "dbo.p": { "paths": { "results[1][]": { "name": "X" } } } } }""");

        Assert.Equal("haruspex.json:1:41: error: procedures[\"dbo.p\"].paths[\"results[1][]\"]: dbo.p returns 1 result set, so there is no results[1].", run.Error);
    }

    [Fact]
    public void A_recursive_function_s_type_is_named_after_the_function()
    {
        const string Tree = """
            CREATE TABLE dbo.node (id int NOT NULL PRIMARY KEY, parent_id int NULL);
            GO
            CREATE FUNCTION dbo.tree(@id int) RETURNS nvarchar(max) AS
            BEGIN
                RETURN (SELECT n.id, json_query(dbo.tree(n.id)) AS children FROM dbo.node AS n WHERE n.parent_id = @id FOR JSON PATH);
            END
            GO
            CREATE PROCEDURE dbo.p AS SELECT n.id, json_query(dbo.tree(n.id)) AS children FROM dbo.node AS n FOR JSON PATH;
            GO
            """;

        Assert.Contains("TreeNode", Apply(Tree, "{}").Document.Types.Keys);

        // The function's schema's settings name it.
        Assert.Contains("DboTreeNode", Apply(Tree, """{ "schemas": { "dbo": { "prefix": "Dbo" } } }""").Document.Types.Keys);

        var run = Apply(Tree, """{ "procedures": { "dbo.p": { "paths": { "[].children[]": { "name": "Child" } } } } }""");
        Assert.Equal(
            "haruspex.json:1:41: error: procedures[\"dbo.p\"].paths[\"[].children[]\"]: the value at results[0][].children[] is the recursive type of dbo.tree, which every procedure that reaches it shares, so it can't be overridden for one of them.",
            run.Error);
    }

    // Nullability.

    [Fact]
    public void Nullable_false_makes_the_value_non_null_and_is_listed_with_its_reason()
    {
        var doc = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].name": { "nullable": false, "why": "every customer has a name" } } } } }""").Document;

        Assert.False(Field(doc, "GetCustomerRow", "name").Nullable);
        Assert.Equal(new IrOverride("sales.get_customer", "results[0][].name", IrOverrideSettings.NotNull, "every customer has a name"), Assert.Single(doc.Overrides));

        // Never null, so never left out: the key is required.
        Assert.Contains("  name: z.string().max(40),\n", ZodEmitter.Emit(doc), StringComparison.Ordinal);
        Assert.Contains("\"setting\": \"not-null\"", IrJsonWriter.Write(doc), StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_false_on_the_whole_result_says_it_is_never_absent()
    {
        var doc = Apply("CREATE PROCEDURE dbo.p AS SELECT TOP (1) c.customer_id AS id FROM sales.customer AS c FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;\nGO\n",
            """{ "procedures": { "dbo.p": { "paths": { "": { "nullable": false, "why": "there is always a customer" } } } } }""").Document;

        Assert.False(doc.Objects[0].Results![0].MayBeAbsent);
    }

    [Fact]
    public void Nullable_false_the_inference_agrees_with_is_a_warning()
    {
        var run = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].id": { "nullable": false, "why": "a key" } } } } }""");

        Assert.Equal(
            ["haruspex.json:1:54: warning: procedures[\"sales.get_customer\"].paths[\"[].id\"]: \"nullable\": false agrees with the inference, which already proves the value at results[0][].id is never null. Remove it."],
            run.Problems);
        Assert.Empty(run.Document.Overrides);
    }

    [Fact]
    public void Nullable_false_on_what_json_text_holds_is_an_error()
    {
        var run = Apply("CREATE PROCEDURE dbo.p AS SELECT (SELECT c.customer_id AS id FROM sales.customer AS c FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS one;\nGO\n",
            """{ "procedures": { "dbo.p": { "paths": { "[].one<json>": { "nullable": false, "why": "x" } } } } }""");

        Assert.Equal("haruspex.json:1:41: error: procedures[\"dbo.p\"].paths[\"[].one<json>\"]: the value at results[0][].one<json> is what JSON text holds, which is never null itself; nothing can be declared about its nullability.", run.Error);
    }

    // Paths.

    [Fact]
    public void A_path_that_does_not_exist_is_an_error_listing_the_keys_there()
    {
        var run = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].nmae": { "nullable": false, "why": "x" } } } } }""");

        Assert.Equal(
            "haruspex.json:1:54: error: procedures[\"sales.get_customer\"].paths[\"[].nmae\"]: the path results[0][].nmae does not exist: the object at results[0][] has no key \"nmae\". Its keys are \"id\", \"name\", \"status\", \"level\", \"notes\", \"lines\".",
            run.Error);
    }

    [Fact]
    public void A_path_through_the_wrong_kind_of_value_is_an_error()
    {
        var run = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].name[]": { "nullable": false, "why": "x" } } } } }""");

        Assert.Equal(
            "haruspex.json:1:54: error: procedures[\"sales.get_customer\"].paths[\"[].name[]\"]: the path results[0][].name[] does not exist: the value at results[0][].name is a string (nvarchar(40)), so it has no items ([]).",
            run.Error);
    }

    [Fact]
    public void A_path_into_unknown_results_is_an_error()
    {
        var run = Apply("CREATE PROCEDURE dbo.p AS EXEC ('SELECT 1');\nGO\n", """{ "procedures": { "dbo.p": { "paths": { "[]": { "name": "X" } } } } }""");

        Assert.Equal("haruspex.json:1:41: error: procedures[\"dbo.p\"].paths[\"[]\"]: the results of dbo.p are unknown (see the unknowns in the IR), so no path in them exists.", run.Error);
    }

    [Fact]
    public void A_path_through_a_union_is_an_error()
    {
        // An object and a nullable array, ABSENT ON NULL: an array whose items are either, as JSON text in a rowset.
        var run = Apply("CREATE PROCEDURE dbo.p AS SELECT json_array(json_object('a': 1), (SELECT l.sku FROM sales.line AS l FOR JSON PATH)) AS u;\nGO\n",
            """{ "procedures": { "dbo.p": { "paths": { "[].u<json>[].a": { "nullable": false, "why": "x" } } } } }""");

        Assert.Equal("haruspex.json:1:41: error: procedures[\"dbo.p\"].paths[\"[].u<json>[].a\"]: the path results[0][].u<json>[].a passes through a value of several types at results[0][].u<json>[], and a path can't choose between them.", run.Error);
    }

    [Fact]
    public void Naming_something_that_is_not_an_object_is_an_error()
    {
        var run = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].lines": { "name": "Lines" } } } } }""");

        Assert.Equal(
            "haruspex.json:1:54: error: procedures[\"sales.get_customer\"].paths[\"[].lines\"]: only an object type can be named, and the value at results[0][].lines is an array. To name its items, use the path \"[].lines[]\".",
            run.Error);
    }

    // Which procedures may be overridden.

    [Fact]
    public void An_override_of_a_procedure_that_does_not_exist_is_an_error()
    {
        var run = Apply(GetCustomer, """{ "procedures": { "sales.get_customr": { "name": "X" } } }""");

        Assert.Equal("haruspex.json:1:19: error: procedures[\"sales.get_customr\"]: there is no procedure sales.get_customr in the project.", run.Error);
    }

    [Fact]
    public void An_override_of_a_procedure_that_is_not_selected_is_an_error()
    {
        var run = Apply(GetCustomer + "CREATE PROCEDURE sales.other AS SELECT 1 AS a;\nGO\n", """{ "procedures": { "sales.other": { "name": "X" } } }""", include: "sales.get_*");

        Assert.Equal(
            "haruspex.json:1:19: error: procedures[\"sales.other\"]: the procedure sales.other is not selected, so it can't be overridden. Overrides apply only to the procedures its schema's \"include\" selects and \"exclude\" leaves in.",
            run.Error);
    }

    [Fact]
    public void An_override_of_a_procedure_a_one_off_include_left_out_is_skipped()
    {
        var run = Apply(GetCustomer + "CREATE PROCEDURE sales.other AS SELECT 1 AS a;\nGO\n", """{ "procedures": { "sales.other": { "name": "X" } } }""",
            include: "sales.get_*", selectedByFile: ["sales.get_customer", "sales.other"]);

        Assert.Empty(run.Problems);
    }

    [Fact]
    public void Two_overrides_of_one_procedure_written_in_different_case_are_an_error()
    {
        var run = Apply(GetCustomer, "{ \"procedures\": {\n\"sales.get_customer\": { \"name\": \"A\" },\n\"SALES.Get_Customer\": { \"name\": \"B\" } } }");

        Assert.Equal("haruspex.json:3:1: error: procedures[\"SALES.Get_Customer\"]: sales.get_customer already has overrides, at line 2; names are compared ignoring case, as SQL Server compares them. Merge the two.", run.Error);
    }

    [Fact]
    public void An_override_matches_its_procedure_ignoring_case() =>
        Assert.Equal("X", Apply(GetCustomer, """{ "procedures": { "SALES.GET_CUSTOMER": { "name": "X" } } }""").Document.Objects[0].Name);

    // Types and imports.

    [Fact]
    public void A_type_override_imports_a_schema_and_clears_the_unknowns_under_it()
    {
        const string Body = "CREATE PROCEDURE dbo.p AS SELECT upper(c.name) AS shout, c.customer_id AS id FROM sales.customer AS c FOR JSON PATH;\nGO\n";
        Assert.Contains(Apply(Body, "{}").Document.Unknowns, u => u.Path == "results[0][].shout");

        var doc = Apply(Body, """{ "procedures": { "dbo.p": { "paths": { "[].shout": { "type": "ShoutSchema", "import": "@app/types", "why": "upper of a name" } } } } }""").Document;

        Assert.Empty(doc.Unknowns);
        Assert.Equal(new IrImport("ShoutSchema", "@app/types"), Field(doc, "PRow", "shout").Type);
        Assert.Equal(new IrOverride("dbo.p", "results[0][].shout", IrOverrideSettings.Type, "upper of a name"), Assert.Single(doc.Overrides));
        var zod = ZodEmitter.Emit(doc);
        Assert.Contains("import { ShoutSchema } from '@app/types'\n", zod, StringComparison.Ordinal);
        Assert.Contains("  shout: ShoutSchema.optional(),\n", zod, StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_with_brackets_is_an_array_of_the_imported_schema()
    {
        var doc = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].lines": { "type": "LineSchema[]", "import": "@app/lines" } } } } }""").Document;

        Assert.Equal(new IrArray(new IrImport("LineSchema", "@app/lines")), Field(doc, "GetCustomerRow", "lines").Type);

        // The object type the override replaced is no longer reached, so it is not written.
        Assert.DoesNotContain("GetCustomerLine", doc.Types.Keys);
    }

    [Theory]
    [InlineData("./types/notes", "out", "../types/notes")]
    [InlineData("../shared/notes", "src/generated", "../../../shared/notes")]
    [InlineData("./types/notes", ".", "./types/notes")]
    [InlineData("@app/notes", "out", "@app/notes")]
    public void A_relative_import_is_written_relative_to_the_output_folder(string import, string output, string expected)
    {
        var doc = Apply(GetCustomer, $$"""{ "procedures": { "sales.get_customer": { "paths": { "[].notes": { "type": "NotesSchema", "import": "{{import}}" } } } } }""", output: output).Document;

        Assert.Equal(new IrImport("NotesSchema", expected), Field(doc, "GetCustomerRow", "notes").Type);
    }

    // Value sets.

    [Fact]
    public void Values_exact_makes_a_collation_set_an_enum()
    {
        var before = ZodEmitter.Emit(Apply(GetCustomer, "{}").Document);
        Assert.Contains("  status: z.string().max(8) /* one of \"open\", \"closed\" as the database compares", before, StringComparison.Ordinal);

        var doc = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].status": { "values": "exact", "why": "written in lower case only" } } } } }""").Document;

        Assert.Contains("  status: z.enum([\"open\", \"closed\"]),\n", ZodEmitter.Emit(doc), StringComparison.Ordinal);
        Assert.Equal(new IrOverride("sales.get_customer", "results[0][].status", IrOverrideSettings.ExactValues, "written in lower case only"), Assert.Single(doc.Overrides));
    }

    [Fact]
    public void Values_exact_on_a_set_already_exact_is_a_warning()
    {
        var run = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].level": { "values": "exact", "why": "x" } } } } }""");

        Assert.Equal(
            ["haruspex.json:1:54: warning: procedures[\"sales.get_customer\"].paths[\"[].level\"]: \"values\": \"exact\" agrees with the inference, which already proves the set at results[0][].level exact. Remove it."],
            run.Problems);
    }

    [Fact]
    public void Values_exact_where_the_ir_has_no_set_is_an_error_and_fakes_none()
    {
        var run = Apply(GetCustomer, """{ "procedures": { "sales.get_customer": { "paths": { "[].name": { "values": "exact", "why": "x" } } } } }""");

        Assert.Equal(
            "haruspex.json:1:54: error: procedures[\"sales.get_customer\"].paths[\"[].name\"]: the value at results[0][].name has no value set to declare exact. haruspex infers one from a CHECK constraint on a column the value passes through unchanged; canonical value sets for other cases are planned, not done.",
            run.Error);
    }

    [Fact]
    public void A_document_with_no_overrides_writes_an_empty_list() =>
        Assert.Contains("\"overrides\": []", IrJsonWriter.Write(Apply(GetCustomer, "{}").Document), StringComparison.Ordinal);
}
