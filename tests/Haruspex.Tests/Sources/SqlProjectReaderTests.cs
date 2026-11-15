using Haruspex.Cli;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;
using Haruspex.SqlServer.Sources.Projects;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Sources;

/// <summary>
/// <see cref="SqlProjectReader"/> on projects written here: which scripts a build reads, with which settings, how much MSBuild is
/// evaluated (decision 0044) and what is reported where it isn't, and how references are read. That a project reads as the
/// package its build makes is proved in <c>ProjectEquivalenceTests</c>.
/// </summary>
public class SqlProjectReaderTests
{
    private static string Projects => Path.Combine(RepositoryPaths.Root, "tests", "dacpacs", "projects");

    private const string Dsp = "<DSP>Microsoft.Data.Tools.Schema.Sql.Sql160DatabaseSchemaProvider</DSP>";

    /// <summary>An SDK-style project of <paramref name="body"/>, with a DSP unless <paramref name="properties"/> replaces it.</summary>
    private static string Sdk(string body = "", string properties = Dsp, string sdk = "Microsoft.Build.Sql/2.3.0") => $"""
        <Project Sdk="{sdk}">
          <PropertyGroup>
            <Name>p</Name>
            {properties}
          </PropertyGroup>
          {body}
        </Project>
        """;

    /// <summary>A legacy project as Visual Studio writes it, its items <paramref name="items"/>.</summary>
    private static string Legacy(string items, string properties = "") => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003" ToolsVersion="4.0">
          <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
          <PropertyGroup>
            <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
            <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
            <Name>p</Name>
            {Dsp}
            {properties}
          </PropertyGroup>
          <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
            <OutputPath>bin\Debug\</OutputPath>
          </PropertyGroup>
          <PropertyGroup>
            <VisualStudioVersion Condition="'$(VisualStudioVersion)' == ''">11.0</VisualStudioVersion>
            <SSDTExists Condition="Exists('$(MSBuildExtensionsPath)\Microsoft\VisualStudio\v$(VisualStudioVersion)\SSDT\Microsoft.Data.Tools.Schema.SqlTasks.targets')">True</SSDTExists>
            <VisualStudioVersion Condition="'$(SSDTExists)' == ''">11.0</VisualStudioVersion>
          </PropertyGroup>
          <Import Condition="'$(SQLDBExtensionsRefPath)' != ''" Project="$(SQLDBExtensionsRefPath)\Microsoft.Data.Tools.Schema.SqlTasks.targets" />
          <Import Condition="'$(SQLDBExtensionsRefPath)' == ''" Project="$(MSBuildExtensionsPath)\Microsoft\VisualStudio\v$(VisualStudioVersion)\SSDT\Microsoft.Data.Tools.Schema.SqlTasks.targets" />
          <ItemGroup>
            {items}
          </ItemGroup>
        </Project>
        """;

    private static string Proc(string name, string body = "SELECT 1 AS one;") => $"CREATE PROCEDURE dbo.{name} AS {body}";

    /// <summary>The project <c>p/p.sqlproj</c> of <paramref name="project"/>, beside the files given, read.</summary>
    private static SqlProjectReadResult Read(TempDirectory folder, string project, params (string Path, string Text)[] files)
    {
        foreach (var (path, text) in files)
            folder.File(path, text);
        return SqlProjectReader.Read(folder.File("p/p.sqlproj", project));
    }

    private static string[] Procedures(SqlProjectReadResult read) => [.. read.Model.Procedures.Select(p => p.Name.Name).Order(StringComparer.Ordinal)];

    private static bool QuotedIdentifierOff(SqlProjectReadResult read, string procedure)
    {
        var body = (SqlBody)read.Model.FindProcedure(new ObjectName("dbo", procedure))!.Body;
        var select = (SelectStatement)body.Statements.Statements.Single();
        var column = (SelectScalarExpression)((QuerySpecification)select.QueryExpression).SelectElements.Single();
        return column.Expression is StringLiteral;
    }

    // Which scripts a build reads.

    [Fact]
    public void An_sdk_style_project_reads_every_sql_file_under_its_folder_but_bin_and_obj_hidden_folders_and_any_case_included()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Sdk(), ("p/a.sql", Proc("a")), ("p/deep/er/b.sql", Proc("b")), ("p/.hidden/c.sql", Proc("c")), ("p/UPPER.SQL", Proc("upper")),
            ("p/bin/Debug/x.sql", Proc("in_bin")), ("p/obj/y.sql", Proc("in_obj")), ("p/notes.txt", "not sql"));

        Assert.Empty(read.Diagnostics);
        Assert.Equal(["a", "b", "c", "upper"], Procedures(read));
    }

    [Fact]
    public void Build_remove_none_and_the_deployment_scripts_are_taken_out_and_so_is_a_file_they_include_with_r()
    {
        using var folder = new TempDirectory();
        var read = Read(
            folder,
            Sdk("""
                <ItemGroup>
                  <Build Remove="gone.sql" />
                  <Build Remove="scratch\**" />
                  <None Include="none.sql" />
                  <PreDeploy Include="deploy\pre.sql" />
                  <PostDeploy Include="deploy\post.sql" />
                </ItemGroup>
                """),
            ("p/kept.sql", Proc("kept")), ("p/gone.sql", Proc("gone")), ("p/scratch/s.sql", Proc("scratch")), ("p/none.sql", Proc("none")),
            ("p/deploy/pre.sql", Proc("pre")), ("p/deploy/post.sql", "PRINT 1;\nGO\n:r .\\seed.sql\n  :R \"sub\\more.sql\"\n"),
            ("p/deploy/seed.sql", Proc("seed")), ("p/deploy/sub/more.sql", ":r ..\\deeper.sql"), ("p/deploy/deeper.sql", Proc("deeper")));

        Assert.Empty(read.Diagnostics);
        Assert.Equal(["kept"], Procedures(read));
    }

    [Fact]
    public void A_content_item_neither_adds_to_build_nor_takes_out_of_it()
    {
        using var folder = new TempDirectory();
        var read = Read(
            folder,
            Sdk("""
                <ItemGroup>
                  <Content Include="inside.sql" />
                  <Content Include="..\outside\out.sql" />
                  <Build Remove="taken.sql" />
                  <Content Include="taken.sql" />
                </ItemGroup>
                """),
            ("p/inside.sql", Proc("inside")), ("outside/out.sql", Proc("outside")), ("p/taken.sql", Proc("taken")));

        Assert.Equal(["inside"], Procedures(read));
    }

    [Fact]
    public void A_build_item_outside_the_folder_is_read_and_located_from_the_project_s_folder()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Sdk("<ItemGroup><Build Include=\"$(Shared)\\shared.sql\" /></ItemGroup>", Dsp + "<Shared>..\\common</Shared>"), ("common/shared.sql", Proc("shared")));

        Assert.Equal(["shared"], Procedures(read));
        Assert.Equal("../common/shared.sql", read.Model.FindProcedure(new ObjectName("dbo", "shared"))!.Span.File);
    }

    [Fact]
    public void Default_items_can_be_turned_off_and_their_excludes_added_to()
    {
        using var folder = new TempDirectory();
        var off = Read(folder, Sdk("<ItemGroup><Build Include=\"one.sql\" /></ItemGroup>", Dsp + "<EnableDefaultSqlItems>False</EnableDefaultSqlItems>"), ("p/one.sql", Proc("one")), ("p/two.sql", Proc("two")));
        using var other = new TempDirectory();
        var excluded = Read(other, Sdk(properties: Dsp + "<DefaultSqlItemExcludes>drafts/**</DefaultSqlItemExcludes><BaseOutputPath>out\\</BaseOutputPath>"),
            ("p/one.sql", Proc("one")), ("p/drafts/d.sql", Proc("draft")), ("p/out/o.sql", Proc("built")), ("p/bin/b.sql", Proc("bin")));

        Assert.Equal(["one"], Procedures(off));
        Assert.Equal(["bin", "one"], Procedures(excluded));
    }

    [Fact]
    public void A_legacy_project_reads_the_scripts_it_lists_and_none_else_and_its_boilerplate_says_nothing()
    {
        using var folder = new TempDirectory();
        var read = Read(
            folder,
            Legacy("""
                <Build Include="Tables\Kiln.sql" />
                <Build Include="Procedures\*.sql" />
                <None Include="Notes.sql" />
                <PostDeploy Include="Post.sql" />
                """),
            ("p/Tables/Kiln.sql", "CREATE TABLE dbo.kiln (id int NOT NULL);"), ("p/Procedures/a.sql", Proc("a")), ("p/Procedures/b.sql", Proc("b")),
            ("p/Notes.sql", Proc("notes")), ("p/Post.sql", "PRINT 1;"), ("p/Stray.sql", Proc("stray")));

        Assert.Empty(read.Diagnostics);
        Assert.Equal(["a", "b"], Procedures(read));
        Assert.NotNull(read.Model.FindTable(new ObjectName("dbo", "kiln")));
    }

    [Fact]
    public void A_path_is_found_whatever_the_case_of_its_names_as_a_build_on_windows_finds_it()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Legacy("<Build Include=\"TABLES\\kiln.SQL\" />"), ("p/Tables/Kiln.sql", "CREATE TABLE dbo.kiln (id int NOT NULL);"));

        Assert.Empty(read.Diagnostics);
        Assert.NotNull(read.Model.FindTable(new ObjectName("dbo", "kiln")));
        Assert.Equal("Tables/Kiln.sql", read.Model.FindTable(new ObjectName("dbo", "kiln"))!.Span.File);
    }

    [Fact]
    public void A_script_a_project_builds_that_isn_t_there_is_an_error_and_so_is_one_rooted_the_windows_way_read_elsewhere()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Legacy("<Build Include=\"Missing.sql\" />" + (OperatingSystem.IsWindows() ? "" : "<Build Include=\"C:\\build\\x.sql\" />")));

        Assert.Contains(read.Diagnostics, d => d.Code == SourceCodes.ScriptNotFound && d.Message.Contains("Missing.sql", StringComparison.Ordinal));
        if (!OperatingSystem.IsWindows())
            Assert.Contains(read.Diagnostics, d => d.Code == SourceCodes.ScriptNotFound && d.Message.Contains(@"C:\build\x.sql", StringComparison.Ordinal) && d.Message.Contains("rooted", StringComparison.Ordinal));
    }

    [Fact]
    public void A_preplan_script_is_built_by_2_0_0_and_not_by_2_3_0_and_an_unobserved_version_says_it_isn_t_known()
    {
        static SqlProjectReadResult With(TempDirectory folder, string version) =>
            Read(folder, Sdk("<ItemGroup><PrePlan Include=\"plan.sql\" /></ItemGroup>", sdk: $"Microsoft.Build.Sql/{version}"), ("p/plan.sql", Proc("planned")), ("p/a.sql", Proc("a")));
        using var a = new TempDirectory();
        using var b = new TempDirectory();
        using var c = new TempDirectory();

        Assert.Equal(["a", "planned"], Procedures(With(a, "2.0.0")));
        Assert.Equal(["a"], Procedures(With(b, "2.3.0")));
        var unknown = With(c, "2.2.0");
        Assert.Equal(["a"], Procedures(unknown));
        Assert.Contains(unknown.Diagnostics, d => d.Code == SourceCodes.PartNotRead && d.Message.Contains("PrePlan", StringComparison.Ordinal));
    }

    // The settings each script starts with.

    [Fact]
    public void A_script_starts_with_its_item_s_settings_or_else_the_project_s()
    {
        using var folder = new TempDirectory();
        var read = Read(
            folder,
            Sdk("""
                <ItemGroup>
                  <Build Update="on.sql" QuotedIdentifier="On" />
                  <Build Update="nulls.sql"><AnsiNulls>off</AnsiNulls></Build>
                </ItemGroup>
                """, Dsp + "<QuotedIdentifier>False</QuotedIdentifier>"),
            ("p/plain.sql", Proc("plain", "SELECT \"ripe\" AS state;")), ("p/on.sql", Proc("quoted", "SELECT \"ripe\" AS state;")), ("p/nulls.sql", Proc("nulls")));

        Assert.True(QuotedIdentifierOff(read, "plain"));
        Assert.False(QuotedIdentifierOff(read, "quoted"));
        Assert.True(read.Model.FindProcedure(new ObjectName("dbo", "nulls"))!.AnsiNullsOff);
        Assert.False(read.Model.FindProcedure(new ObjectName("dbo", "plain"))!.AnsiNullsOff);
    }

    [Fact]
    public void Item_definitions_give_every_later_item_its_metadata()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Legacy("<Build Include=\"a.sql\" />").Replace("<ItemGroup>", "<ItemDefinitionGroup><Build><AnsiNulls>Off</AnsiNulls></Build></ItemDefinitionGroup><ItemGroup>", StringComparison.Ordinal), ("p/a.sql", Proc("a")));

        Assert.True(read.Model.FindProcedure(new ObjectName("dbo", "a"))!.AnsiNullsOff);
    }

    [Fact]
    public void A_setting_neither_on_nor_off_is_reported_and_its_script_not_read()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Legacy("<Build Include=\"a.sql\" AnsiNulls=\"Maybe\" /><Build Include=\"b.sql\" />"), ("p/a.sql", Proc("a")), ("p/b.sql", Proc("b")));

        Assert.Equal(["b"], Procedures(read));
        Assert.Contains(read.Diagnostics, d => d.Code == SourceCodes.PartNotRead && d.Message.Contains("\"Maybe\"", StringComparison.Ordinal));
    }

    // The database's settings.

    [Fact]
    public void A_project_that_sets_nothing_has_the_settings_a_build_gives_it()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Sdk(), ("p/a.sql", Proc("a")));

        Assert.Equal(new DatabaseSettings("SQL_Latin1_General_CP1_CI_AS", 160, true, true, true, true, true, true, false, true), read.Model.Settings);
    }

    [Fact]
    public void A_project_s_settings_are_its_properties()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Sdk(properties: Dsp + """
            <DefaultCollation>Latin1_General_100_CS_AS</DefaultCollation>
            <CompatibilityMode>130</CompatibilityMode>
            <AnsiNulls>false</AnsiNulls><AnsiNullDefault>False</AnsiNullDefault><AnsiPadding>False</AnsiPadding><AnsiWarnings>False</AnsiWarnings>
            <ArithAbort>False</ArithAbort><ConcatNullYieldsNull>False</ConcatNullYieldsNull><NumericRoundAbort>True</NumericRoundAbort><QuotedIdentifier>False</QuotedIdentifier>
            """));

        Assert.Equal(new DatabaseSettings("Latin1_General_100_CS_AS", 130, false, false, false, false, false, false, true, false), read.Model.Settings);
    }

    [Fact]
    public void A_project_without_a_dsp_is_reported_and_its_level_is_compatibility_mode_or_unknown()
    {
        using var folder = new TempDirectory();
        var bare = Read(folder, Sdk(properties: ""));
        using var other = new TempDirectory();
        var given = Read(other, Sdk(properties: "<CompatibilityMode>150</CompatibilityMode>"));
        using var azure = new TempDirectory();
        var cloud = Read(azure, Sdk(properties: "<DSP>Microsoft.Data.Tools.Schema.Sql.SqlAzureV12DatabaseSchemaProvider</DSP>"));

        Assert.Contains(bare.Diagnostics, d => d.Code == SourceCodes.PartNotRead && d.Message.Contains("names no DSP", StringComparison.Ordinal));
        Assert.Null(bare.Model.Settings!.CompatibilityLevel);
        Assert.Equal(150, given.Model.Settings!.CompatibilityLevel);
        Assert.Null(cloud.Model.Settings!.CompatibilityLevel);
        Assert.Empty(cloud.Diagnostics);
    }

    [Fact]
    public void A_setting_from_the_nearest_directory_build_props_is_read_and_one_further_up_only_if_it_imports_it()
    {
        using var folder = new TempDirectory();
        folder.File("Directory.Build.props", "<Project><PropertyGroup><DefaultCollation>Latin1_General_100_CI_AI</DefaultCollation><AnsiWarnings>False</AnsiWarnings></PropertyGroup></Project>");
        folder.File("p/Directory.Build.props", "<Project><PropertyGroup><ArithAbort>False</ArithAbort></PropertyGroup></Project>");
        var nearest = Read(folder, Sdk());
        folder.File("p/Directory.Build.props", "<Project><Import Project=\"$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))\" /><PropertyGroup><ArithAbort>False</ArithAbort></PropertyGroup></Project>");
        var importing = Read(folder, Sdk());

        Assert.Equal("SQL_Latin1_General_CP1_CI_AS", nearest.Model.Settings!.Collation);
        Assert.False(nearest.Model.Settings.ArithAbort);
        Assert.True(nearest.Model.Settings.AnsiWarnings);
        Assert.Contains(importing.Diagnostics, d => d.Code == SourceCodes.PartNotRead && d.Message.Contains("property function", StringComparison.Ordinal) && d.At is SourceLocation { File: "Directory.Build.props" });
    }

    [Fact]
    public void A_project_reads_the_same_wherever_it_is_copied()
    {
        using var folder = new TempDirectory();
        foreach (var project in (string[])["orchard", "orchard-beyond", "press", "cellar"])
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(Projects, project), "*", SearchOption.AllDirectories))
            {
                var copy = Path.Combine(folder.Path, project, Path.GetRelativePath(Path.Combine(Projects, project), file));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(file, copy);
            }
        }

        var here = SqlProject.LoadProject(Path.Combine(Projects, "orchard", "orchard.sqlproj"));
        var there = SqlProject.LoadProject(Path.Combine(folder.Path, "orchard", "orchard.sqlproj"));

        Assert.Empty(here.Diagnostics);
        Assert.Empty(there.Diagnostics);
        Assert.Equal(IrJsonWriter.Write(here.Infer(here.Procedures).Expected()), IrJsonWriter.Write(there.Infer(there.Procedures).Expected()));
    }

    // What isn't evaluated: each names itself once, and only what it touches isn't read.

    public static TheoryData<string, string, string> Unevaluated { get; } = new()
    {
        { "a property function", "<Build Include=\"$([System.IO.Path]::Combine('x', 'a.sql'))\" />", "the property function $([System.IO.Path]::Combine('x', 'a.sql')) isn't evaluated" },
        { "an item list", "<Build Include=\"@(Other)\" />", "the item list @(Other) isn't evaluated" },
        { "a property defined nowhere", "<Build Include=\"$(Elsewhere)\\a.sql\" />", "$(Elsewhere) is defined nowhere in the project" },
        { "a condition it can't evaluate", "<Build Include=\"a.sql\" Condition=\"$([MSBuild]::IsOSPlatform('Windows'))\" />", "isn't evaluated" },
        { "a condition function it doesn't know", "<Build Include=\"a.sql\" Condition=\"Exists('a.sql') And IsWindows()\" />", "the condition function IsWindows() isn't evaluated" },
        { "metadata batching", "<Build Include=\"a.sql\" Condition=\"'%(Extension)' == '.sql'\" />", "the metadata %(Extension) isn't evaluated" },
        { "a Remove it can't evaluate", "<Build Remove=\"$(Elsewhere)\" />", "$(Elsewhere) is defined nowhere in the project" },
    };

    [Theory]
    [MemberData(nameof(Unevaluated))]
    public void An_item_that_isn_t_evaluated_names_itself_once_and_only_it_isn_t_read(string what, string item, string why)
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Legacy(item + "<Build Include=\"b.sql\" />"), ("p/a.sql", Proc("a")), ("p/b.sql", Proc("b")));

        _ = what;
        var reported = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.PartNotRead, reported.Code);
        Assert.Contains(why, reported.Message, StringComparison.Ordinal);
        Assert.StartsWith("p.sqlproj:", reported.At!.ToString(), StringComparison.Ordinal);
        Assert.Equal(["b"], Procedures(read));
    }

    [Fact]
    public void A_setting_that_isn_t_evaluated_names_itself_once_and_the_rest_are_read()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Sdk(properties: Dsp + "<DefaultCollation>Latin1_General_100_CI_AS</DefaultCollation><ArithAbort>$(FromTheBuildServer)</ArithAbort>"), ("p/a.sql", Proc("a")));

        var reported = Assert.Single(read.Diagnostics);
        Assert.Contains("ArithAbort, a setting of the database, isn't read: $(FromTheBuildServer) is defined nowhere", reported.Message, StringComparison.Ordinal);
        Assert.Null(read.Model.Settings);
        Assert.Equal("Latin1_General_100_CI_AS", read.Model.DefaultCollation);
        Assert.Equal(["a"], Procedures(read));
    }

    [Fact]
    public void A_choose_doubts_what_it_sets_and_nothing_else()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Sdk("""
            <Choose>
              <When Condition="'$(Configuration)' == 'Debug'"><PropertyGroup><DefaultCollation>Latin1_General_100_CI_AS</DefaultCollation></PropertyGroup></When>
              <Otherwise><ItemGroup><None Include="a.sql" /></ItemGroup></Otherwise>
            </Choose>
            """), ("p/a.sql", Proc("a")), ("p/b.sql", Proc("b")));

        Assert.Equal(2, read.Diagnostics.Count(d => d.Message.Contains("Choose isn't evaluated", StringComparison.Ordinal)));
        Assert.Null(read.Model.DefaultCollation);
        Assert.Equal(["a", "b"], Procedures(read));
    }

    [Fact]
    public void An_import_that_isn_t_found_or_evaluated_is_reported_and_one_that_is_is_read()
    {
        using var folder = new TempDirectory();
        var read = Read(
            folder,
            Sdk("""
                <Import Project="shared.props" />
                <Import Project="missing.props" />
                <Import Project="$(Elsewhere)\x.props" />
                <Import Project="*.props" />
                """),
            ("p/shared.props", "<Project><PropertyGroup><DefaultCollation>Latin1_General_100_CI_AI</DefaultCollation></PropertyGroup></Project>"));

        Assert.Equal("Latin1_General_100_CI_AI", read.Model.DefaultCollation);
        Assert.Equal(3, read.Diagnostics.Count(d => d.Code == SourceCodes.PartNotRead && d.Message.StartsWith("The import of", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_target_with_something_in_it_is_reported_as_not_read()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Sdk("<Target Name=\"AddScripts\" BeforeTargets=\"Build\"><ItemGroup><Build Include=\"extra.sql\" /></ItemGroup></Target><Target Name=\"Empty\" />"));

        var reported = Assert.Single(read.Diagnostics);
        Assert.Contains("The target AddScripts runs during a build", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void What_a_property_or_condition_touches_that_isn_t_read_says_nothing()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Sdk("<ItemGroup Condition=\"'$(GITHUB_ACTIONS)' == 'true'\"><PackageDownload Include=\"x\" /></ItemGroup>", Dsp + "<Odd>$([System.DateTime]::Now)</Odd><Ci Condition=\"$(Weird)\">1</Ci>"), ("p/a.sql", Proc("a")));

        Assert.Empty(read.Diagnostics);
    }

    [Fact]
    public void What_differs_as_release_is_reported_and_debug_is_read()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Sdk("<ItemGroup Condition=\"'$(Configuration)' == 'Release'\"><Build Remove=\"debug_only.sql\" /></ItemGroup>"), ("p/a.sql", Proc("a")), ("p/debug_only.sql", Proc("debug_only")));

        Assert.Equal(["a", "debug_only"], Procedures(read));
        var reported = Assert.Single(read.Diagnostics);
        Assert.Contains("builds differently as Release than as Debug: the scripts debug_only.sql", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_of_another_sdk_isn_t_read()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Sdk(sdk: "MSBuild.Sdk.SqlProj/3.0.0"), ("p/a.sql", Proc("a")));

        var reported = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.UnreadableProject, reported.Code);
        Assert.Empty(read.Model.Procedures);
    }

    [Fact]
    public void A_file_that_isn_t_a_project_isn_t_read()
    {
        using var folder = new TempDirectory();
        var read = SqlProjectReader.Read(folder.File("p.sqlproj", "<NotAProject />"));

        Assert.Equal(SourceCodes.UnreadableProject, Assert.Single(read.Diagnostics).Code);
    }

    [Fact]
    public void Csharp_files_a_project_compiles_are_reported_as_not_read()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Legacy("<Compile Include=\"Clr.cs\" /><Build Include=\"a.sql\" />"), ("p/a.sql", Proc("a")), ("p/Clr.cs", "class C { }"));

        Assert.Contains("compiles C# files", Assert.Single(read.Diagnostics).Message, StringComparison.Ordinal);
    }

    // References.

    [Fact]
    public void A_project_of_the_same_database_is_read_with_its_own_settings_transitively_and_each_once()
    {
        using var folder = new TempDirectory();
        folder.File("q/q.sqlproj", Sdk("<ItemGroup><ProjectReference Include=\"..\\r\\r.sqlproj\" /></ItemGroup>", Dsp + "<QuotedIdentifier>False</QuotedIdentifier>"));
        folder.File("q/q.sql", Proc("in_q", "SELECT \"ripe\" AS state;"));
        folder.File("r/r.sqlproj", Sdk());
        folder.File("r/r.sql", Proc("in_r", "SELECT \"ripe\" AS state;"));
        var read = Read(
            folder,
            Sdk("<ItemGroup><ProjectReference Include=\"../q/q.sqlproj\" /><ProjectReference Include=\"../R/R.sqlproj\" /></ItemGroup>", Dsp + "<DefaultCollation>Latin1_General_100_CI_AS</DefaultCollation>"),
            ("p/p.sql", Proc("in_p", "SELECT \"ripe\" AS state;")));

        Assert.Empty(read.Diagnostics);
        Assert.Equal(["in_p", "in_q", "in_r"], Procedures(read));
        Assert.Equal(3, read.Projects.Count);
        Assert.False(QuotedIdentifierOff(read, "in_p"));
        Assert.True(QuotedIdentifierOff(read, "in_q"));
        Assert.False(QuotedIdentifierOff(read, "in_r"));
        Assert.Equal("Latin1_General_100_CI_AS", read.Model.Settings!.Collation);
        Assert.Equal("../q/q.sql", read.Model.FindProcedure(new ObjectName("dbo", "in_q"))!.Span.File);
    }

    [Fact]
    public void A_project_of_the_same_database_not_found_is_one_warning_and_the_model_says_what_it_lacks()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Sdk("<ItemGroup><ProjectReference Include=\"..\\gone\\gone.sqlproj\" /></ItemGroup>"), ("p/a.sql", Proc("a")));

        var reported = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.ProjectNotFound, reported.Code);
        Assert.Equal([new UnreadPart("gone.sqlproj", "p.sqlproj")], read.Model.Unread);
    }

    [Fact]
    public void An_object_two_projects_define_is_defined_twice()
    {
        using var folder = new TempDirectory();
        folder.File("q/q.sqlproj", Sdk());
        folder.File("q/a.sql", Proc("a"));
        var read = Read(folder, Sdk("<ItemGroup><ProjectReference Include=\"..\\q\\q.sqlproj\" /></ItemGroup>"), ("p/a.sql", Proc("a")));

        Assert.Equal(SourceCodes.DefinedTwice, Assert.Single(read.Diagnostics).Code);
    }

    [Fact]
    public void A_reference_to_a_project_that_isn_t_a_database_project_is_passed_over()
    {
        using var folder = new TempDirectory();
        var read = Read(folder, Sdk("<ItemGroup><ProjectReference Include=\"..\\clr\\Clr.csproj\" /></ItemGroup>"), ("p/a.sql", Proc("a")));

        Assert.Empty(read.Diagnostics);
    }

    [Fact]
    public void A_reference_read_in_a_later_slice_is_one_note_each_and_a_name_in_it_is_unknown_as_from_a_folder()
    {
        const string reads = "CREATE PROCEDURE dbo.reads AS SELECT e.amount FROM [$(Ledger)].dbo.entry AS e;";
        using var folder = new TempDirectory();
        folder.File("ledger/ledger.sqlproj", Sdk());
        var read = Read(
            folder,
            Sdk("""
                <ItemGroup>
                  <ProjectReference Include="..\ledger\ledger.sqlproj"><DatabaseSqlCmdVariable>Ledger</DatabaseSqlCmdVariable></ProjectReference>
                  <ArtifactReference Include="..\lib\lib.dacpac" />
                  <PackageReference Include="Microsoft.SqlServer.Dacpacs.Master" Version="170.0.4" />
                  <SqlCmdVariable Include="Ledger"><DefaultValue>ledger</DefaultValue></SqlCmdVariable>
                </ItemGroup>
                """),
            ("p/reads.sql", reads));
        using var scripts = new TempDirectory();
        scripts.File("reads.sql", reads);

        Assert.Equal(3, read.Diagnostics.Count);
        Assert.All(read.Diagnostics, d => Assert.Equal(SourceCodes.ReferenceNotRead, d.Code));
        var fromProject = SqlProject.LoadProject(Path.Combine(folder.Path, "p", "p.sqlproj"), collation: null);
        var fromFolder = SqlProject.Load(scripts.Path, fromProject.DefaultCollation);
        Assert.Equal(IrJsonWriter.Write(fromFolder.Infer(fromFolder.Procedures).Expected()), IrJsonWriter.Write(fromProject.Infer(fromProject.Procedures).Expected()));
    }
}
