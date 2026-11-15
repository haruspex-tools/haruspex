# A project's references are read from where its build takes them

**Date:** 15 Nov 2026

**Decision.** A `.sqlproj`'s references to other databases and to DACPACs are read as a DACPAC's are
([decision 0040](0040-a-dacpac-is-read-with-the-packages-of-its-database.md), [decision 0042](0042-a-table-of-another-database-is-read-as-its-package-declares-it.md)),
each from where the project's build takes it, as observed with `Microsoft.Build.Sql` 2.3.0 and 1.0.0 and with SSDT's build targets
for a legacy project:
- **Another database's project** (`ProjectReference` with `DatabaseSqlCmdVariable` or `DatabaseVariableLiteralValue`) is read from
  its own project file, with the projects and packages of its own database, into a model of its own under the name a body writes:
  `$(VaultDb)` for a variable, `almanac` for a literal. One level down, as from a DACPAC.
- **A DACPAC** (`ArtifactReference`) is its `Include`; its `HintPath` isn't used. With a database named, it is read into a model
  of its own; without, into this database's model, with the packages of its database it refers to.
- **A NuGet package's DACPAC** (`PackageReference`, in a `Microsoft.Build.Sql` project) is its `HintPath`, or else
  `tools/<name>.dacpac` in the folder the restore gave its version: the cache NuGet uses (`NUGET_PACKAGES`, or `.nuget/packages`
  in the user's home), or the project's `RestorePackagesPath`; `<name>` is its `DacpacName`, else `master` or `msdb` for
  `Microsoft.SqlServer.Dacpacs.*`, else its id. A system database's package is that database unless it names another. A package
  restored without that file is no reference; a version that isn't one version (`170.*`, a range, none in the project) isn't
  read, and is reported.
- **Visual Studio's own system packages** (`$(DacPacRootPath)\…\SqlSchemas\master.dacpac`), when the project doesn't define
  `$(DacPacRootPath)`, are read by their file name from a `references` folder of the config file.
- **Any DACPAC not where its build takes it** is looked for by its file name in the config file's `references` folders, in order.
  One found nowhere is a warning ([HAR1003](../diagnostics/HAR1003.md)) naming every place looked; the IR records it without a hash.
- **A variable is taken by its name**, never its value. **A database on another server** (`ServerSqlCmdVariable`) isn't read, from
  a project or a DACPAC, and is a note ([HAR1012](../diagnostics/HAR1012.md)). A reference that names its database both ways isn't
  read, and is reported.

The IR's `source.references` lists the packages a project read, found or not, with their hashes, as a DACPAC's does; its
`source.package` is left out, as for a folder, since a project's scripts aren't hashed. [HAR1011](../diagnostics/HAR1011.md), the
note that said these references weren't read yet, is retired.

**Context.** A project was read as one database, its other references noted, so a procedure that read another database or a
system view was typed from its DACPAC and unknown from its project. What a build does with each reference was observed with probe
projects, built and read back:
- A package's header records a referenced project as `ExternalParts` `[$(LedgerDb)]` or `[almanac]`, a server too as
  `[$(Srv)].[$(Db)]`, a DACPAC by its full path, the master package as `[master]` with or without `DatabaseVariableLiteralValue`.
- A `DatabaseSqlCmdVariable` naming a variable the project doesn't declare builds with the same `[$(RackDb)]`; every declared
  variable's value is recorded empty, whatever `<Value>` gives. A body's `[$(LedgerDb)]` is kept as written; the same name without its
  brackets, `$(LedgerDb).books.entry`, fails the build (SQL46010).
- An `ArtifactReference` whose `Include` isn't there fails the build (SQL72027) even with a `HintPath` that is; a referenced project
  that isn't there fails it too (MSB3202).
- Visual Studio's form under a .NET SDK build resolves to Microsoft.Build.Sql's own `tools/net8.0`, which holds no DACPAC
  (SQL72027); given a `$(DacPacRootPath)` holding it, it builds.
- The SDK's `Sdk.targets` (1.0.0 and 2.3.0 alike) makes a reference of a package's DACPAC only if the file
  is there: a `DacpacName` naming no file built with no reference and no warning. `170.*` restored 170.0.4. A legacy project's
  `PackageReference` to the master package restored and gave no reference: SSDT's targets don't read one.

**Alternatives.**
- *Read a referenced project's package from its `bin` folder.* It is what the build reads, but only once that project is built,
  and may be an older build than its scripts; reading the scripts needs no build, and the proof shows the two agree.
- *Find a package by any version the cache holds.* A cache holds whatever any project on the machine restored, and NuGet chooses a
  version against a feed, which isn't read; a version other than the project's would be read in silence.
- *Bind a variable by its default value.* A default is what a deployment uses when given nothing; the package records no value,
  and a body writes the variable, not its value, so the reference's name is what binds.
- *Look for Visual Studio's install.* It is where the file is, on Windows with Visual Studio, and nowhere else; a `references`
  folder works the same everywhere, CI included.

**Reason.** A project read now gives what its built DACPAC gives, read with the same referenced packages, apart from locations and
the source record (`ProjectEquivalenceTests`). Each place a reference is looked for is one its build uses, or one the user names;
whatever the project form leaves open (a floating version, a database named twice, another server) is said, not guessed.

**See also:** [The inputs and the model](../internals/02-the-inputs-and-the-model.md), "A project file becomes the same model";
`ProjectReferences`, `ProjectReading`, `PackageSet`, `PackageCache`; [decision 0044](0044-a-project-file-is-evaluated-in-part-and-the-rest-reported.md).
