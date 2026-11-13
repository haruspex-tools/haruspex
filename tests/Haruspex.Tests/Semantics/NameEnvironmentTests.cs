using Haruspex.SqlServer.Semantics;

namespace Haruspex.Tests.Semantics;

/// <summary>
/// The names in force at a statement compare by value where binding keeps what it bound for each set of names
/// (<see cref="Bindings"/>): two environments built apart with the same temporary tables' columns are one, so a statement read
/// again is not bound again. A temporary table's name compares ignoring case, as SQL Server's does.
/// </summary>
public class NameEnvironmentTests
{
    private static NameEnvironment With(string table, params string[] columns) =>
        NameEnvironment.Empty.WithTemporaryColumns([KeyValuePair.Create(new LocalName(table), (IReadOnlyList<string>)[.. columns])]);

    [Fact]
    public void Environments_built_apart_with_the_same_columns_are_equal()
    {
        Assert.Equal(With("#lines", "id", "qty"), With("#LINES", "id", "qty"));
        Assert.Equal(With("#lines", "id", "qty").GetHashCode(), With("#LINES", "id", "qty").GetHashCode());
        Assert.NotEqual(With("#lines", "id", "qty"), With("#lines", "qty", "id"));
        Assert.NotEqual(With("#lines", "id"), NameEnvironment.Empty);
    }

    [Fact]
    public void Its_equality_keeps_the_laws() =>
        EqualityLaws.Hold([
            NameEnvironment.Empty, NameEnvironment.View, With("#lines", "id"), With("#Lines", "id"), With("#lines", "id"),
            With("#lines", "id", "qty"), With("#other", "id"),
        ]);
}
