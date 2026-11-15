using Haruspex.Cli;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;
using Haruspex.SqlServer.Sources.Projects;

namespace Haruspex.Tests.Sources;

/// <summary>
/// What a project refers to, read as its build takes it (decision 0045): another database's project, a DACPAC by its path or in a
/// NuGet package, of the project's own database or another, and Visual Studio's own <c>master.dacpac</c>. The packages are the
/// ones in <c>tests/dacpacs</c>: harbour's, built by DacFx, and the stand-in in <c>master.dacpac</c>'s shape.
/// </summary>
public partial class SqlProjectReaderTests
{
    private static string Dacpacs => Path.Combine(RepositoryPaths.Root, "tests", "dacpacs");

    private static string MasterShape => Path.Combine(Dacpacs, "master-shape");

    /// <summary>
    /// The project <c>p/p.sqlproj</c> of <paramref name="project"/>, beside the files given, read with <paramref name="search"/> as
    /// the config file's <c>references</c> and the package cache in <c>packages</c>, which holds only what the test puts there.
    /// </summary>
    private static SqlProjectReadResult ReadWith(TempDirectory folder, string project, IReadOnlyList<string>? search = null, params (string Path, string Text)[] files)
    {
        foreach (var (path, text) in files)
            folder.File(path, text);
        return SqlProjectReader.Read(folder.File("p/p.sqlproj", project), search ?? [], new PackageCache(Path.Combine(folder.Path, "packages")));
    }

    /// <summary>Copies a package of <c>tests/dacpacs</c> to <paramref name="to"/>, under <paramref name="folder"/>.</summary>
    private static void Copy(TempDirectory folder, string from, string to)
    {
        var target = Path.Combine(folder.Path, to);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(Path.Combine(Dacpacs, from), target);
    }

    /// <summary>The stand-in for <c>master.dacpac</c>, where a restore of the master package at <paramref name="version"/> puts it.</summary>
    private static void Restored(TempDirectory folder, string version = "170.0.0") =>
        Copy(folder, Path.Combine("master-shape", "master.dacpac"), Path.Combine("packages", "microsoft.sqlserver.dacpacs.master", version, "tools", "master.dacpac"));

    private static string[] Tables(DatabaseModel model) => [.. model.Tables.Select(t => t.Name.ToString()).Order(StringComparer.Ordinal)];

    private const string Vault = """
        <ItemGroup>
          <ProjectReference Include="..\vault\vault.sqlproj"><DatabaseSqlCmdVariable>VaultDb</DatabaseSqlCmdVariable></ProjectReference>
          <SqlCmdVariable Include="VaultDb"><DefaultValue>vault</DefaultValue><Value>$(SqlCmdVar__1)</Value></SqlCmdVariable>
        </ItemGroup>
        """;

    private static void VaultProject(TempDirectory folder, string items = "")
    {
        folder.File("vault/vault.sqlproj", Sdk(items, Dsp + "<DefaultCollation>Latin1_General_100_CS_AS</DefaultCollation>"));
        folder.File("vault/entry.sql", "CREATE TABLE books.entry (entry_id int NOT NULL, memo varchar(20) NULL);");
    }

    [Fact]
    public void Another_database_s_project_is_read_into_a_model_of_its_own_by_the_name_a_body_writes_it_by()
    {
        using var folder = new TempDirectory();
        VaultProject(folder);
        folder.File("almanac/almanac.sqlproj", Sdk());
        folder.File("almanac/season.sql", "CREATE TABLE dbo.season (season_id int NOT NULL);");
        var read = ReadWith(
            folder,
            Sdk(Vault + """<ItemGroup><ProjectReference Include="../almanac/almanac.sqlproj" DatabaseVariableLiteralValue="almanac" /></ItemGroup>"""),
            files: ("p/a.sql", Proc("a")));

        Assert.Empty(read.Diagnostics);
        Assert.Equal(["$(VaultDb)", "almanac"], read.Model.Databases.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["books.entry"], Tables(read.Model.Databases["$(VaultDb)"]));
        Assert.Equal("Latin1_General_100_CS_AS", read.Model.Databases["$(VaultDb)"].Settings!.Collation);
        Assert.Equal(["dbo.season"], Tables(read.Model.Databases["almanac"]));
        Assert.Empty(read.Model.Tables);
        Assert.Equal(["p.sqlproj", "vault.sqlproj", "almanac.sqlproj"], read.Projects.Select(Path.GetFileName));
        Assert.Equal("../vault/entry.sql", read.Model.Databases["$(VaultDb)"].Tables.Single().Span.File);
    }

    [Fact]
    public void A_variable_names_its_database_by_its_name_whether_or_not_the_project_declares_it_and_never_by_its_value()
    {
        using var folder = new TempDirectory();
        VaultProject(folder);
        var undeclared = ReadWith(folder, Sdk("""<ItemGroup><ProjectReference Include="..\vault\vault.sqlproj" DatabaseSqlCmdVariable="VaultDb" /></ItemGroup>"""));
        using var other = new TempDirectory();
        VaultProject(other);
        var valued = ReadWith(other, Sdk(Vault.Replace("$(SqlCmdVar__1)", "elsewhere", StringComparison.Ordinal)));

        Assert.Equal(["$(VaultDb)"], undeclared.Model.Databases.Keys);
        Assert.Equal(["$(VaultDb)"], valued.Model.Databases.Keys);
    }

    [Fact]
    public void Another_database_is_read_one_level_down_and_its_own_references_to_a_third_are_not()
    {
        using var folder = new TempDirectory();
        VaultProject(folder, """<ItemGroup><ProjectReference Include="../almanac/almanac.sqlproj" DatabaseVariableLiteralValue="almanac" /></ItemGroup>""");
        folder.File("almanac/almanac.sqlproj", Sdk());
        var read = ReadWith(folder, Sdk(Vault));

        Assert.Empty(read.Diagnostics);
        Assert.Empty(read.Model.Databases["$(VaultDb)"].Databases);
        Assert.DoesNotContain("almanac.sqlproj", read.Projects.Select(Path.GetFileName));
    }

    [Fact]
    public void A_dacpac_of_the_same_database_is_read_into_the_model_with_the_packages_of_its_database_it_refers_to()
    {
        using var folder = new TempDirectory();
        Copy(folder, Path.Combine("harbour", "crane.dacpac"), Path.Combine("lib", "crane.dacpac"));
        Copy(folder, Path.Combine("harbour", "quay.dacpac"), Path.Combine("lib", "quay.dacpac"));
        var read = ReadWith(folder, Sdk("""<ItemGroup><ArtifactReference Include="..\lib\crane.dacpac" /></ItemGroup>"""));

        Assert.Empty(read.Diagnostics);
        Assert.Contains("dock.crane", Tables(read.Model));
        Assert.Contains("dock.berth", Tables(read.Model));
        Assert.Equal("crane.dacpac/[dock].[crane]", read.Model.FindTable(new ObjectName("dock", "crane"))!.Span.File);
        Assert.Equal(["crane.dacpac", "quay.dacpac"], read.References.Select(r => r.Name));
        Assert.All(read.References, r => Assert.NotNull(r.Found));
        Assert.All(read.References, r => Assert.Null(r.Database));
    }

    [Fact]
    public void A_dacpac_of_another_database_is_read_into_a_model_of_its_own()
    {
        using var folder = new TempDirectory();
        Copy(folder, Path.Combine("harbour", "tally.dacpac"), Path.Combine("lib", "tally.dacpac"));
        var read = ReadWith(folder, Sdk("""<ItemGroup><ArtifactReference Include="../lib/tally.dacpac"><DatabaseSqlCmdVariable>Tally</DatabaseSqlCmdVariable></ArtifactReference></ItemGroup>"""));

        Assert.Empty(read.Diagnostics);
        Assert.Equal(["ledger.entry"], Tables(read.Model.Databases["$(Tally)"]));
        var reference = Assert.Single(read.References);
        Assert.Equal(("tally.dacpac", "$(Tally)"), (reference.Name, reference.Database));
    }

    [Fact]
    public void A_dacpac_not_found_is_one_warning_naming_where_it_was_looked_for_and_is_recorded_without_a_hash()
    {
        using var folder = new TempDirectory();
        const string Gone = """<ItemGroup><ArtifactReference Include="../lib/gone.dacpac" /></ItemGroup>""";
        folder.File("q/q.sqlproj", Sdk(Gone));
        var read = ReadWith(folder, Sdk(Gone + """<ItemGroup><ProjectReference Include="../q/q.sqlproj" /></ItemGroup>"""));

        var reported = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.ReferenceNotFound, reported.Code);
        Assert.Contains($"p.sqlproj refers to the DACPAC ../lib/gone.dacpac, of its own database, which isn't found: looked for at {Path.Combine(folder.Path, "lib", "gone.dacpac")}.", reported.Message, StringComparison.Ordinal);
        var reference = Assert.Single(read.References);
        Assert.Equal(("gone.dacpac", null), (reference.Name, reference.Found));
        Assert.Contains(new UnreadPart("gone.dacpac", "p.sqlproj"), read.Model.Unread);
    }

    [Fact]
    public void An_artifact_is_where_its_include_says_and_its_hint_path_isn_t_used_as_its_build_observed()
    {
        using var folder = new TempDirectory();
        Copy(folder, Path.Combine("harbour", "tally.dacpac"), Path.Combine("lib", "tally.dacpac"));
        var read = ReadWith(folder, Sdk("""<ItemGroup><ArtifactReference Include="../lib/gone.dacpac"><HintPath>../lib/tally.dacpac</HintPath></ArtifactReference></ItemGroup>"""));

        Assert.Equal(SourceCodes.ReferenceNotFound, Assert.Single(read.Diagnostics).Code);
        Assert.Empty(read.Model.Tables);
    }

    [Fact]
    public void A_dacpac_not_where_its_build_takes_it_is_found_by_its_name_in_a_references_folder()
    {
        using var folder = new TempDirectory();
        Copy(folder, Path.Combine("harbour", "tally.dacpac"), Path.Combine("shared", "tally.dacpac"));
        var read = ReadWith(
            folder,
            Sdk("""<ItemGroup><ArtifactReference Include="..\elsewhere\tally.dacpac" DatabaseVariableLiteralValue="tally" /></ItemGroup>"""),
            [Path.Combine(folder.Path, "shared")]);

        Assert.Empty(read.Diagnostics);
        Assert.Equal(Path.Combine(folder.Path, "shared", "tally.dacpac"), Assert.Single(read.References).Found!.Path);
        Assert.Equal(["ledger.entry"], Tables(read.Model.Databases["tally"]));
    }

    [Theory]
    [InlineData("170.0.0")]
    [InlineData("170.0")]
    [InlineData("170.0.0.0")]
    public void The_master_package_s_dacpac_is_read_from_where_its_restore_put_it_as_the_database_master(string version)
    {
        using var folder = new TempDirectory();
        Restored(folder);
        var read = ReadWith(folder, Sdk($"""<ItemGroup><PackageReference Include="Microsoft.SqlServer.Dacpacs.Master" Version="{version}" /></ItemGroup>"""));

        Assert.Empty(read.Diagnostics);
        Assert.True(read.Model.Databases.ContainsKey("master"));
        var reference = Assert.Single(read.References);
        Assert.Equal(("master.dacpac", "master"), (reference.Name, reference.Database));
        Assert.EndsWith(Path.Combine("170.0.0", "tools", "master.dacpac"), reference.Found!.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void A_package_restored_without_the_dacpac_its_build_looks_for_is_no_reference_as_its_build_makes_none()
    {
        using var folder = new TempDirectory();
        Restored(folder);
        folder.File("packages/rules.package/1.0.0/lib/netstandard2.1/Rules.dll", "");
        Copy(folder, Path.Combine("harbour", "tally.dacpac"), Path.Combine("packages", "shared.schema", "2.0.0", "tools", "other.dacpac"));
        var read = ReadWith(folder, Sdk("""
            <ItemGroup>
              <PackageReference Include="Rules.Package" Version="1.0.0" />
              <PackageReference Include="Shared.Schema" Version="2.0.0" />
              <PackageReference Include="Microsoft.SqlServer.Dacpacs.Master" Version="170.0.0" DacpacName="absent" />
            </ItemGroup>
            """));

        Assert.Empty(read.Diagnostics);
        Assert.Empty(read.References);
        Assert.Empty(read.Model.Databases);
    }

    [Fact]
    public void A_package_s_dacpac_is_named_by_its_id_or_dacpac_name_or_given_by_its_hint_path_and_of_the_project_s_database_unless_it_names_another()
    {
        using var folder = new TempDirectory();
        Copy(folder, Path.Combine("harbour", "tally.dacpac"), Path.Combine("packages", "shared.schema", "2.0.0", "tools", "Shared.Schema.dacpac"));
        Copy(folder, Path.Combine("harbour", "crane.dacpac"), Path.Combine("packages", "cranes", "1.0.0", "tools", "crane.dacpac"));
        Copy(folder, Path.Combine("harbour", "quay.dacpac"), Path.Combine("packages", "cranes", "1.0.0", "tools", "quay.dacpac"));
        Copy(folder, Path.Combine("harbour", "tally.dacpac"), Path.Combine("lib", "tally.dacpac"));
        var read = ReadWith(folder, Sdk("""
            <ItemGroup>
              <PackageReference Include="Shared.Schema" Version="2.0.0" />
              <PackageReference Include="Cranes" Version="1.0.0" DacpacName="crane" />
              <PackageReference Include="Tally.Package" Version="9.9.9" HintPath="../lib/tally.dacpac" DatabaseVariableLiteralValue="tally" />
            </ItemGroup>
            """));

        Assert.Empty(read.Diagnostics);
        Assert.Contains("ledger.entry", Tables(read.Model));
        Assert.Contains("dock.crane", Tables(read.Model));
        Assert.Equal(["ledger.entry"], Tables(read.Model.Databases["tally"]));
    }

    [Theory]
    [InlineData("Version=\"170.*\"", "its version 170.* is a range or a floating version")]
    [InlineData("Version=\"[170.0.0,)\"", "its version [170.0.0,) is a range or a floating version")]
    [InlineData("", "it gives no version")]
    public void A_package_whose_version_isn_t_one_version_in_the_project_is_reported_and_not_read(string version, string why)
    {
        using var folder = new TempDirectory();
        Restored(folder);
        var read = ReadWith(folder, Sdk($"""<ItemGroup><PackageReference Include="Microsoft.SqlServer.Dacpacs.Master" {version} /></ItemGroup>"""));

        var reported = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.PartNotRead, reported.Code);
        Assert.Contains(why, reported.Message, StringComparison.Ordinal);
        Assert.Empty(read.References);
    }

    [Fact]
    public void A_package_not_restored_is_a_warning_saying_to_restore_unless_a_references_folder_holds_its_dacpac()
    {
        const string Master = """<ItemGroup><PackageReference Include="Microsoft.SqlServer.Dacpacs.Master" Version="170.0.0" /></ItemGroup>""";
        using var folder = new TempDirectory();
        var missing = ReadWith(folder, Sdk(Master));
        using var other = new TempDirectory();
        var found = ReadWith(other, Sdk(Master), [MasterShape]);

        var reported = Assert.Single(missing.Diagnostics);
        Assert.Equal(SourceCodes.ReferenceNotFound, reported.Code);
        Assert.Contains("refers to master.dacpac of the package Microsoft.SqlServer.Dacpacs.Master 170.0.0, the database master, which isn't found", reported.Message, StringComparison.Ordinal);
        Assert.EndsWith("Restore the project, or name a folder holding master.dacpac in \"references\" in the config file.", reported.Message, StringComparison.Ordinal);
        Assert.Empty(found.Diagnostics);
        Assert.True(found.Model.Databases.ContainsKey("master"));
    }

    [Fact]
    public void A_restore_folder_the_project_names_is_where_its_packages_are()
    {
        using var folder = new TempDirectory();
        Copy(folder, Path.Combine("master-shape", "master.dacpac"), Path.Combine("p", "restored", "microsoft.sqlserver.dacpacs.master", "170.0.0", "tools", "master.dacpac"));
        var read = ReadWith(folder, Sdk(
            """<ItemGroup><PackageReference Include="Microsoft.SqlServer.Dacpacs.Master" Version="170.0.0" /></ItemGroup>""",
            Dsp + "<RestorePackagesPath>restored</RestorePackagesPath>"));

        Assert.Empty(read.Diagnostics);
        Assert.True(read.Model.Databases.ContainsKey("master"));
    }

    [Fact]
    public void A_legacy_project_s_package_reference_is_no_reference_as_its_build_makes_none()
    {
        using var folder = new TempDirectory();
        Restored(folder);
        var read = ReadWith(
            folder,
            Legacy("""<Build Include="a.sql" /><PackageReference Include="Microsoft.SqlServer.Dacpacs.Master" Version="170.0.0" />"""),
            files: ("p/a.sql", Proc("a")));

        Assert.Empty(read.Diagnostics);
        Assert.Empty(read.References);
        Assert.Empty(read.Model.Databases);
    }

    private const string VisualStudioMaster = """
        <Build Include="a.sql" />
        <ArtifactReference Include="$(DacPacRootPath)\Extensions\Microsoft\SQLDB\Extensions\SqlServer\150\SqlSchemas\master.dacpac">
          <HintPath>$(DacPacRootPath)\Extensions\Microsoft\SQLDB\Extensions\SqlServer\150\SqlSchemas\master.dacpac</HintPath>
          <SuppressMissingDependenciesErrors>False</SuppressMissingDependenciesErrors>
          <DatabaseVariableLiteralValue>master</DatabaseVariableLiteralValue>
        </ArtifactReference>
        """;

    [Fact]
    public void Visual_studio_s_own_master_is_read_from_a_references_folder_and_said_when_it_isn_t_there()
    {
        using var folder = new TempDirectory();
        var found = ReadWith(folder, Legacy(VisualStudioMaster), [MasterShape], ("p/a.sql", Proc("a")));
        using var other = new TempDirectory();
        var missing = ReadWith(other, Legacy(VisualStudioMaster), null, ("p/a.sql", Proc("a")));

        Assert.Empty(found.Diagnostics);
        Assert.Equal(("master.dacpac", "master"), (Assert.Single(found.References).Name, found.References[0].Database));
        Assert.True(found.Model.Databases.ContainsKey("master"));
        var reported = Assert.Single(missing.Diagnostics);
        Assert.Equal(SourceCodes.ReferenceNotFound, reported.Code);
        Assert.Contains("p.sqlproj refers to SSDT's own master.dacpac, the database master, which isn't found. Only a Visual Studio install has it", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Visual_studio_s_own_master_is_read_where_the_project_says_when_it_defines_where()
    {
        using var folder = new TempDirectory();
        Copy(folder, Path.Combine("master-shape", "master.dacpac"), Path.Combine("vs", "Extensions", "Microsoft", "SQLDB", "Extensions", "SqlServer", "150", "SqlSchemas", "master.dacpac"));
        var read = ReadWith(folder, Legacy(VisualStudioMaster, "<DacPacRootPath>..\\vs</DacPacRootPath>"), null, ("p/a.sql", Proc("a")));

        Assert.Empty(read.Diagnostics);
        Assert.StartsWith(Path.Combine(folder.Path, "vs"), Assert.Single(read.References).Found!.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_to_a_database_on_another_server_is_one_note_and_isn_t_read()
    {
        using var folder = new TempDirectory();
        VaultProject(folder);
        var read = ReadWith(folder, Sdk("""<ItemGroup><ProjectReference Include="..\vault\vault.sqlproj" DatabaseSqlCmdVariable="VaultDb" ServerSqlCmdVariable="Annexe" /></ItemGroup>"""));

        var reported = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.OtherServer, reported.Code);
        Assert.StartsWith("p.sqlproj refers to the project ..\\vault\\vault.sqlproj, the database $(VaultDb) on the server $(Annexe), which isn't read", reported.Message, StringComparison.Ordinal);
        Assert.Empty(read.Model.Databases);
        Assert.Equal(["p.sqlproj"], read.Projects.Select(Path.GetFileName));
    }

    [Fact]
    public void A_reference_naming_its_database_both_ways_is_reported_and_not_read()
    {
        using var folder = new TempDirectory();
        VaultProject(folder);
        var read = ReadWith(folder, Sdk("""<ItemGroup><ProjectReference Include="..\vault\vault.sqlproj" DatabaseSqlCmdVariable="VaultDb" DatabaseVariableLiteralValue="vault" /></ItemGroup>"""));

        var reported = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.PartNotRead, reported.Code);
        Assert.Contains("names its database both by DatabaseSqlCmdVariable and by DatabaseVariableLiteralValue", reported.Message, StringComparison.Ordinal);
        Assert.Empty(read.Model.Databases);
    }

    [Fact]
    public void Another_database_s_project_not_found_is_one_warning_and_this_database_lacks_nothing()
    {
        using var folder = new TempDirectory();
        var read = ReadWith(folder, Sdk(Vault));

        var reported = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.ProjectNotFound, reported.Code);
        Assert.StartsWith("p.sqlproj refers to the project ..\\vault\\vault.sqlproj, of the database $(VaultDb), which isn't found", reported.Message, StringComparison.Ordinal);
        Assert.Empty(read.Model.Unread);
        Assert.Empty(read.Model.Databases);
    }

    [Fact]
    public void A_reference_made_only_as_release_is_reported_and_read_as_debug()
    {
        using var folder = new TempDirectory();
        Copy(folder, Path.Combine("harbour", "tally.dacpac"), Path.Combine("lib", "tally.dacpac"));
        var read = ReadWith(folder, Sdk("""<ItemGroup Condition="'$(Configuration)' == 'Release'"><ArtifactReference Include="../lib/tally.dacpac" DatabaseVariableLiteralValue="tally" /></ItemGroup>"""));

        var reported = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.PartNotRead, reported.Code);
        Assert.Contains("builds differently as Release than as Debug: what it refers to", reported.Message, StringComparison.Ordinal);
        Assert.Empty(read.Model.Databases);
    }

    [Fact]
    public void The_ir_of_a_project_records_the_packages_it_refers_to_and_no_package_of_its_own()
    {
        using var folder = new TempDirectory();
        Copy(folder, Path.Combine("harbour", "tally.dacpac"), Path.Combine("lib", "tally.dacpac"));
        var path = folder.File("p/p.sqlproj", Sdk("""<ItemGroup><ArtifactReference Include="../lib/tally.dacpac" DatabaseSqlCmdVariable="Tally" /></ItemGroup>"""));
        folder.File("p/p.sql", "CREATE PROCEDURE dbo.p AS SELECT e.fee FROM [$(Tally)].ledger.entry AS e;");
        var read = SqlProjectReader.Read(path, [], new PackageCache(Path.Combine(folder.Path, "packages")));
        var document = SqlProject.LoadProject(path, read).Infer([new ObjectName("dbo", "p")]).Expected();

        Assert.Null(document.Source.Package);
        var reference = Assert.Single(document.Source.References);
        Assert.Equal(("tally.dacpac", "$(Tally)", read.References[0].Found!.Sha256), (reference.Name, reference.Database, reference.Sha256));
        Assert.Empty(document.Unknowns);
    }
}
