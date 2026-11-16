# Security policy

## Reporting a vulnerability

Report a vulnerability privately, through GitHub's private vulnerability reporting: on the repository's **Security** tab,
choose **Report a vulnerability**, or go straight to <https://github.com/sharpblue/haruspex/security/advisories/new>. Please
don't open a public issue or pull request for it. Say what an attacker could do, which commit you found it at, and how to
reproduce it, with an invented example where you can rather than a real database's code.

This is a personal project with [no support commitment](SUPPORT.md), so there is no promised response time. A confirmed
vulnerability is fixed in the open once the fix is ready, with a security advisory that credits you unless you would rather
not be named.

## Supported versions

The tool is pre-release and has no published versions yet. A fix goes to the latest commit on `main`, and nowhere else.

## What haruspex touches

As a static analyser, haruspex reads a database's source and writes code, and that is all it does.

- **It reads source files only.** `haruspex generate` reads its config file (`haruspex.json`) and the JSON Schema files it
  names; the source it is given (a folder of `.sql` files, a `.sqlproj` with the scripts, MSBuild files and DACPACs its build
  would read, or a DACPAC with the packages it refers to); and, to find a referenced DACPAC, the folders the config file lists
  and the NuGet package folders (`NUGET_PACKAGES`, `.nuget/packages` in your home folder, or the project's
  `RestorePackagesPath`).
- **It never connects to a database**, and makes no network connection of any kind. It needs no credentials and reads no
  data ([decision 0001](docs/decisions/0001-infer-from-source-never-a-live-database.md)).
- **It runs nothing it reads.** No SQL is executed, a project file is evaluated by haruspex itself without MSBuild (so no
  task or property function in it runs), and no process is started.
- **It writes only to its output folder**, `out` in the config file or `--out`: the two files `haruspex.ir.json` and
  `haruspex.zod.ts`, each first written to a temporary file beside it and then moved into place. It creates the folder if it
  isn't there, and writes nothing when the input, the command line or the config file has an error. A run stopped while it
  writes can leave its temporary files in that folder, as [the command line's README](src/Haruspex.Cli/README.md#what-it-guarantees)
  says.

The tests are another matter: the differential tests deploy fixtures to a SQL Server you name, as
[CONTRIBUTING.md](CONTRIBUTING.md#differential-tests-against-sql-server) explains, and some tests run Node.js and npm. None of
that is part of the tool.

So a vulnerability is anything that breaks one of the statements above: for example, a project, DACPAC or config file that
makes haruspex write outside its output folder, read files beyond those listed, or run code; or SQL whose names or comments
make the generated module do something other than validate data, such as code injected through an identifier.
