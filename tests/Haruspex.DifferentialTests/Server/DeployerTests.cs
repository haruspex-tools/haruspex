namespace Haruspex.DifferentialTests.Server;

public class DeployerTests
{
    [Fact]
    public void Scripts_split_on_go_lines_only() =>
        Assert.Equal(
            ["CREATE SCHEMA a;\n", "\nSELECT 'GO' AS going;\n", "\nSELECT 1;\n"],
            Deployer.Batches("CREATE SCHEMA a;\ngo\nSELECT 'GO' AS going;\n  GO -- next\nSELECT 1;\nGO\n"));
}
