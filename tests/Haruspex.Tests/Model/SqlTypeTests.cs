using Haruspex.SqlServer.Model;

namespace Haruspex.Tests.Model;

/// <summary>A declared type compares as it is written: kind, name, each argument in order, and schema.</summary>
public class SqlTypeTests
{
    [Fact]
    public void Types_written_alike_are_equal_and_hash_equal()
    {
        var a = new SqlType(SqlTypeKind.BuiltIn, "decimal", ["19", "2"]);
        var b = new SqlType(SqlTypeKind.BuiltIn, "decimal", [.. new List<string> { "19", "2" }]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, a with { Arguments = ["2", "19"] });
        Assert.NotEqual(a, a with { Schema = "sales" });
    }

    [Fact]
    public void Its_equality_keeps_the_laws() =>
        EqualityLaws.Hold([
            new SqlType(SqlTypeKind.BuiltIn, "decimal", ["19", "2"]), new SqlType(SqlTypeKind.BuiltIn, "decimal", ["19", "2"]),
            new SqlType(SqlTypeKind.BuiltIn, "decimal", ["19"]), new SqlType(SqlTypeKind.Named, "price", [], "sales"),
            new SqlType(SqlTypeKind.Named, "price", [], "sales"), new SqlType(SqlTypeKind.Named, "price", []),
        ]);
}
