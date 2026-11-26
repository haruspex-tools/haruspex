using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// What a document's source means for its types, in the words every emitter's header uses: here, the packages the config file
/// declared builds (<see cref="IrSource.Builds"/>), said only when there are some.
/// </summary>
public class IrSourceTextTests
{
    [Fact]
    public void A_source_with_no_build_declared_says_nothing() =>
        Assert.Null(IrSourceText.Builds(new IrSource("tsql") { Baseline = new IrSource("tsql") }));

    [Fact]
    public void One_build_is_named_alone()
    {
        Assert.Equal(
            "Its types assume that sales.dacpac was built from a project, as the config file declares, so its primary keys and unique constraints hold.",
            IrSourceText.Builds(new IrSource("tsql") { Builds = ["sales.dacpac"] }));
    }

    [Fact]
    public void Several_builds_are_listed_the_source_s_first_then_the_baseline_s()
    {
        var source = new IrSource("tsql") { Builds = ["stock.dacpac", "sales.dacpac"], Baseline = new IrSource("tsql") { Builds = ["sales.dacpac"] } };

        Assert.Equal(
            "Its types assume that sales.dacpac, stock.dacpac and the baseline's sales.dacpac were built from projects, as the config file declares, so their primary keys and unique constraints hold.",
            IrSourceText.Builds(source));
    }
}
