# A project file is evaluated in part, and the rest reported

**Date:** 15 Nov 2026

**Decision.** A `.sqlproj` is read with no MSBuild, no DacFx and no build, and as much of MSBuild is evaluated as a database
project's build reads:
- **Two passes, in MSBuild's order.** First every property, in document order, across the project and the files it imports;
  then the item definitions and the items, with the properties' final values.
- **Properties.** `$(Name)` references, and MSBuild's reserved properties (`MSBuildProjectDirectory`,
  `MSBuildThisFileDirectory` and the rest). `Configuration` and `Platform` are empty until set.
- **Conditions.** `==` and `!=` (ignoring case), `<`, `>`, `<=` and `>=` of numbers, `and`, `or`, `!`, parentheses, MSBuild's
  words for true and false, `Exists('path')` (relative to the project's folder) and `HasTrailingSlash('path')`.
- **Items.** `Include` with `Exclude`, `Remove` and `Update`, with MSBuild's patterns (`*`, `?`, `**`); metadata as attributes
  and as elements, each under its own condition; `ItemDefinitionGroup`.
- **Imports.** The nearest `Directory.Build.props` and `Directory.Build.targets` above the project, which both formats import
  through MSBuild's common files; and an `Import` of a file that is there, evaluated the same way. The SDK's and SSDT's own
  files are recognised by their names and stood for by what they do that the database's build reads: for
  `Microsoft.Build.Sql`, the default `Build` item `**/*.sql` less `$(DefaultSqlItemExcludes)` and the output folders, and the
  `Build Remove` of `None`, the deployment scripts and (from 2.3.0) `PrePlan`, as its `Sdk.props` and `Sdk.targets` say in 1.0.0,
  2.0.0 and 2.3.0, and the files a deployment script includes with `:r`, which its build takes out as it runs: a line that starts
  with `:r`, not one in a comment or a string (observed). Visual Studio's newer template, with its lines for a build by the .NET
  SDK (`NetCoreBuild`, and a `PackageReference` to `Microsoft.NETFramework.ReferenceAssemblies`), is recognised too, and so is
  its reference to SSDT's own `master.dacpac` under `$(DacPacRootPath)`, a reference to `master` like any other.
- **The configuration.** A project is read as `Debug`, as `dotnet build` builds it, and again as `Release`; where the two
  differ in a script, a setting or a reference, that is a warning naming the difference.
- **Paths** are read with either slash, and a name matches ignoring case everywhere. A folder linked from the project's is walked
  as the build walks it, each once: a link back to a folder already walked isn't entered again.

Whatever isn't evaluated is a doubt on what it touches: a property function (`$([System.IO.Path]::Combine(…))`), an item list
(`@(Build)`), metadata batching (`%(Extension)`), a property defined nowhere in the project or its imports, a condition beyond
the ones above, `Choose`, `KeepMetadata` and its kind. A doubt is reported once ([HAR1008](../diagnostics/HAR1008.md)) when it
reaches what the reader reads: which scripts are built, the settings a script starts with, the database's settings, or a
reference; and that is not read. A doubt that reaches nothing read says nothing. A doubted item that may take a script out of
the build (a `Build Remove`, and for `Microsoft.Build.Sql` a `None`, a deployment script or a `PrePlan`) or change the settings
it starts with (a `Build Update`) leaves out every script it may touch: the ones it names, with a deployment script's `:r`
includes, when only whether it applies is doubted; every script of the project when what it names is doubted too, since it
could name any. The warning names them, or says that no script of the project is read. An import that isn't found or evaluated, and a
target with something in it, are reported whatever they touch, since what they do could be anything. A target is reported once by
its name, however many projects define it. A project of another SDK is refused ([HAR1010](../diagnostics/HAR1010.md)).

**Context.** A real application of about 115 legacy SSDT projects, read as folders, gets no collation, so none of the value
sets its packages make enums. Its project files carry the collation, and say which of the files beside them are built. A
project file is MSBuild, a language with its own expressions, functions and imports, which a database project uses little of:
properties, conditions on the configuration, items, and the SDK's or SSDT's targets.

**Alternatives.**
- *Run MSBuild.* It needs the .NET SDK where haruspex runs, and evaluating a project runs code from it (property functions,
  tasks in imported targets), on a machine that may not trust it. Building it needs DacFx, which haruspex can't take on
  ([decision 0004](0004-scriptdom-is-the-only-sql-dependency.md)).
- *Evaluate all of it.* MSBuild's property functions reach .NET's file system, environment and registry; a second implementation
  of them is a project of its own, and would still differ from the machine that builds.
- *Report everything not evaluated.* A shared `Directory.Build.props` of C# settings (this repository's own is one) would warn
  on every database project beside it, about properties no database build reads.
- *Pass over what isn't evaluated.* The list of scripts, or a collation, could then be other than the build's, in silence.
- *Match paths by case as the file system does.* A build on Linux was observed to ignore case in patterns (`**/*.sql` builds
  `UPPER.SQL`) and in `Remove` (`X\Gone.sql` removes `x/gone.sql`), but to stop at an explicit path whose case differs from
  the file (SQL72001). A project written on Windows builds there; on Linux such a project builds nothing, so following Linux
  would read nothing of a project that does build, where it is built.

**Reason.** A database project's build reads a handful of MSBuild's features, and each is evaluated as MSBuild does, observed
against packages its builds made (`ProjectEquivalenceTests`). Carrying each doubt to what it touches keeps the claim exact:
what is read is the build's, and what can't be known is said where it matters, once, with where it is in which file.

**See also:** [The inputs and the model](../internals/02-the-inputs-and-the-model.md), "A project file becomes the same model";
`ProjectEvaluator`, `ProjectFiles`, `ProjectSettings`; [Configuration](../configuration.md), "Reading a project file".
