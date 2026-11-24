# Security policy

## Reporting a vulnerability

Report a vulnerability privately, through GitHub's private vulnerability reporting: on the repository's **Security** tab,
choose **Report a vulnerability**, or go straight to <https://github.com/haruspex-tools/haruspex/security/advisories/new>. Please
don't open a public issue or pull request for it. Say what an attacker could do, which commit you found it at, and how to
reproduce it, with an invented example where you can rather than a real database's code.

This is a personal project with [no support commitment](SUPPORT.md), so there is no promised response time. A confirmed
vulnerability is fixed in the open once the fix is ready, with a security advisory that credits you unless you would rather
not be named.

## Supported versions

The tool is pre-release and has no released versions yet; the only builds are previews, the npm packages a CI run makes. A
fix goes to the latest commit on `main`, and nowhere else.

## What haruspex touches

As a static analyser, haruspex reads a database's source and writes code, and that is all it does.

- **It reads source files only.** `haruspex generate` and `haruspex check` read each config file they are given
  (`haruspex.json`, or one per module) and the JSON Schema files it names; the source it is given (a folder of `.sql` files, a
  `.sqlproj` with the scripts, MSBuild files and DACPACs its build would read, or a DACPAC with the packages it refers to,
  looked for beside it, in the folders the config file lists, and at the path recorded when it was built); the application's
  query files that `queries` names; and, to find a referenced DACPAC, the folders the config file lists and the NuGet package
  folders (`NUGET_PACKAGES`, `.nuget/packages` in your home folder, or the project's `RestorePackagesPath`). `check` also
  reads the files already in the output folder, to compare with them. `haruspex init` looks through the folders below the
  working directory, four deep, for the database, and reads the one it finds to list its schemas.
- **It never connects to a database**, and opens no network connection of its own. It needs no credentials and reads no data
  ([decision 0001](docs/decisions/0001-infer-from-source-never-a-live-database.md)). A path a DACPAC records for a package it
  refers to is never followed when it names a share or a device (`\\server\share\…`, `//server/share/…`, `\\?\…`, `\\.\…`,
  `\??\…`), since on Windows looking for a file there contacts that server; the warning that the package isn't found says so.
  Any other path haruspex reads, whether a package recorded it or you gave it on the command line or in a config file, is read
  through whatever the file system puts behind it: a recorded `Z:\build\…` on a mapped network drive is read from that drive,
  and a share you name yourself is read from the share.
- **It runs nothing it reads.** No SQL is executed, a project file is evaluated by haruspex itself without MSBuild (so no
  task or property function in it runs), and the binary starts no process. The npm package's launcher starts one: the
  haruspex binary from the platform package installed beside it, with the arguments it was given and no shell. Neither npm
  package has an install script.
- **It writes only what it is asked for.** `generate` writes to its output folder, `out` in the config file or `--out`:
  `haruspex.ir.json`, and beside it the files `emit` names (`haruspex.zod.ts`, `haruspex.jsonschema.json`,
  `haruspex.mssql.ts`), each first written to a temporary file in that folder and then moved into place. It creates the folder
  if it isn't there, and writes nothing when the input, the command line or the config file has an error. A run stopped while
  it writes can leave its temporary files in that folder, as [the command line's README](src/Haruspex.Cli/README.md#what-it-guarantees)
  says. `check` writes no file. `init` writes one, `haruspex.json` in the working directory, and never over one that is
  there. The report goes to standard output and standard error, in every `--format`.

The tests are another matter: the differential tests deploy fixtures to a SQL Server you name, as
[CONTRIBUTING.md](CONTRIBUTING.md#differential-tests-against-sql-server) explains, and some tests run Node.js and npm. None of
that is part of the tool.

So a vulnerability is anything that breaks one of the statements above: for example, a project, DACPAC or config file that
makes haruspex write outside its output folder, read files beyond those listed, or run code. The generated code counts too:
SQL whose names or comments make a generated module do something other than what it is for is a vulnerability, such as code
injected through an identifier, a schema that accepts what its type doesn't, or a function in `haruspex.mssql.ts` that sends
anything but the procedure or query file it is named for, with the parameters it checked.
