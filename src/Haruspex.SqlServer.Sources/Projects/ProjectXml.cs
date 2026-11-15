namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// The words of a project file the reader reads: MSBuild's elements, and the items, properties and metadata a database
/// project's build reads. Each is one constant, so that the reader and its documentation name the same word.
/// </summary>
internal static class ProjectXml
{
    // MSBuild's elements and attributes.
    public const string Project = "Project";
    public const string PropertyGroup = "PropertyGroup";
    public const string ItemGroup = "ItemGroup";
    public const string ItemDefinitionGroup = "ItemDefinitionGroup";
    public const string Import = "Import";
    public const string ImportGroup = "ImportGroup";
    public const string Choose = "Choose";
    public const string Target = "Target";
    public const string Sdk = "Sdk";
    public const string Condition = "Condition";
    public const string Include = "Include";
    public const string Exclude = "Exclude";
    public const string Remove = "Remove";
    public const string Update = "Update";

    // The attributes of an item that change how it applies, which aren't evaluated.
    public const string KeepMetadata = "KeepMetadata";
    public const string RemoveMetadata = "RemoveMetadata";
    public const string KeepDuplicates = "KeepDuplicates";
    public const string MatchOnMetadata = "MatchOnMetadata";
    public const string MatchOnMetadataOptions = "MatchOnMetadataOptions";

    // MSBuild's reserved properties, which every project and file has.
    public const string MSBuildProjectDirectory = "MSBuildProjectDirectory";
    public const string MSBuildProjectFullPath = "MSBuildProjectFullPath";
    public const string MSBuildProjectFile = "MSBuildProjectFile";
    public const string MSBuildProjectName = "MSBuildProjectName";
    public const string MSBuildProjectExtension = "MSBuildProjectExtension";
    public const string MSBuildThisFileDirectory = "MSBuildThisFileDirectory";
    public const string MSBuildThisFileFullPath = "MSBuildThisFileFullPath";
    public const string MSBuildThisFile = "MSBuildThisFile";
    public const string MSBuildThisFileName = "MSBuildThisFileName";
    public const string MSBuildThisFileExtension = "MSBuildThisFileExtension";

    // The items a database project's build reads.
    public const string Build = "Build";
    public const string None = "None";
    public const string Content = "Content";
    public const string PreDeploy = "PreDeploy";
    public const string PostDeploy = "PostDeploy";
    public const string PrePlan = "PrePlan";
    public const string BuildExtensionConfiguration = "BuildExtensionConfiguration";
    public const string DeploymentExtensionConfiguration = "DeploymentExtensionConfiguration";
    public const string ProjectReference = "ProjectReference";
    public const string ArtifactReference = "ArtifactReference";
    public const string PackageReference = "PackageReference";
    public const string Compile = "Compile";

    // The metadata of a Build item that sets how its script starts, and of a reference that names another database.
    public const string AnsiNulls = "AnsiNulls";
    public const string QuotedIdentifier = "QuotedIdentifier";
    public const string DatabaseVariableLiteralValue = "DatabaseVariableLiteralValue";
    public const string DatabaseSqlCmdVariable = "DatabaseSqlCmdVariable";
    public const string ServerSqlCmdVariable = "ServerSqlCmdVariable";
    public const string Version = "Version";
    public const string DacpacName = "DacpacName";
    public const string HintPath = "HintPath";

    // The properties: the database's settings, and those of the SDK's default items.
    public const string DSP = "DSP";
    public const string DefaultCollation = "DefaultCollation";
    public const string CompatibilityMode = "CompatibilityMode";
    public const string AnsiNullDefault = "AnsiNullDefault";
    public const string AnsiPadding = "AnsiPadding";
    public const string AnsiWarnings = "AnsiWarnings";
    public const string ArithAbort = "ArithAbort";
    public const string ConcatNullYieldsNull = "ConcatNullYieldsNull";
    public const string NumericRoundAbort = "NumericRoundAbort";
    public const string EnableDefaultSqlItems = "EnableDefaultSqlItems";
    public const string DefaultSqlItemExcludes = "DefaultSqlItemExcludes";
    public const string BaseOutputPath = "BaseOutputPath";
    public const string BaseIntermediateOutputPath = "BaseIntermediateOutputPath";
    public const string Configuration = "Configuration";
    public const string Platform = "Platform";
    public const string Debug = "Debug";
    public const string Release = "Release";
    public const string ImportDirectoryBuildProps = "ImportDirectoryBuildProps";
    public const string ImportDirectoryBuildTargets = "ImportDirectoryBuildTargets";
    public const string RestorePackagesPath = "RestorePackagesPath";

    // The SDK, and the files MSBuild imports on its own.
    public const string MicrosoftBuildSql = "Microsoft.Build.Sql";
    public const string DirectoryBuildProps = "Directory.Build.props";
    public const string DirectoryBuildTargets = "Directory.Build.targets";
}
