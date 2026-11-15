using Haruspex.SqlServer.Sources.Projects;

namespace Haruspex.Tests.Sources;

/// <summary>
/// <see cref="PackageCache"/>: the folder NuGet restores a package's version into, named as NuGet names it, and nothing for a version
/// only a restore against a feed can settle.
/// </summary>
public class PackageCacheTests
{
    [Theory]
    [InlineData("170.0.0", "170.0.0")]
    [InlineData("170.0", "170.0.0")]
    [InlineData("170", "170.0.0")]
    [InlineData("170.0.0.0", "170.0.0")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData("01.002.3", "1.2.3")]
    [InlineData("1.0.0-Preview.2", "1.0.0-preview.2")]
    [InlineData("1.0.0+build.7", "1.0.0")]
    [InlineData(" 2.0.0 ", "2.0.0")]
    public void A_version_is_normalised_as_nuget_names_its_folder(string written, string folder) =>
        Assert.Equal(folder, PackageCache.Normalised(written));

    [Theory]
    [InlineData("170.*")]
    [InlineData("[170.0.0,)")]
    [InlineData("(1.0,2.0]")]
    [InlineData("$(MasterVersion)")]
    [InlineData("")]
    public void A_range_a_floating_version_or_anything_but_one_version_has_no_folder(string written) =>
        Assert.Null(PackageCache.VersionFolder("cache", "Microsoft.SqlServer.Dacpacs.Master", written));

    [Fact]
    public void A_package_s_folder_is_its_id_in_lower_case_under_the_cache() =>
        Assert.Equal(Path.Join("cache", "microsoft.sqlserver.dacpacs.master", "170.0.4"), PackageCache.VersionFolder("cache", "Microsoft.SqlServer.Dacpacs.Master", "170.0.4"));
}
