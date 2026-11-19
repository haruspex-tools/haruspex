namespace Haruspex.Tests.Emit.Zod;

/// <summary>A table type's row follows the rules for shapes: in the module of the one module that takes it, or else shared.</summary>
public partial class ZodModulesTests
{
    private static readonly (string File, string Sql)[] TableValued =
    [
        ("schemas.sql", Schemas),
        ("types.sql", "CREATE TYPE sales.line_list AS TABLE (id int NOT NULL);\nGO\nCREATE TYPE admin.user_list AS TABLE (name nvarchar(20) NOT NULL);"),
        ("add_lines.sql", "CREATE PROCEDURE sales.add_lines @lines sales.line_list READONLY AS SELECT 1 AS one;"),
        ("price_lines.sql", "CREATE PROCEDURE shop.price_lines @lines sales.line_list READONLY AS SELECT 2 AS two;"),
        ("add_users.sql", "CREATE PROCEDURE admin.add_users @users admin.user_list READONLY AS SELECT 3 AS three;"),
    ];

    [Fact]
    public void A_table_type_s_row_two_modules_take_is_in_the_shared_file_and_one_only_one_takes_in_its_module()
    {
        var modules = Split(InferProcedure.Infer("*.*", TableValued));

        Assert.Contains("LineListRowSchema", Exports(modules["shared"]));
        Assert.Contains("UserListRowSchema", Exports(modules["admin"]));
        Assert.DoesNotContain("LineListRowSchema", Exports(modules["sales"]));
        Assert.Contains("LineListRowSchema", modules["sales"], StringComparison.Ordinal); // imported from the shared file
    }
}
