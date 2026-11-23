# The licences that go with the binary

The native binary that CI builds (Native AOT, in the `native` job) is more than haruspex. It contains the .NET runtime,
with the third-party components the runtime's own notices list, and the two libraries the command uses: ScriptDom, which
parses T-SQL, and System.CommandLine, which reads the command's arguments. Each is under a licence that asks for its
copyright notice and licence text to go with every copy, so every copy of the binary carries them: the `native` job's
artifact and each platform's npm package hold a `licenses` folder beside the binary, and haruspex's own `LICENSE`.

That folder has one folder per package, named as NuGet names a package's folder, `<id>.<version>`:

| Folder | Holds | From |
|---|---|---|
| `Microsoft.NETCore.App.Runtime.NativeAOT.<rid>.<version>` | the .NET runtime's `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` | the runtime pack the publish linked, copied at publish |
| `runtime.<rid>.Microsoft.DotNet.ILCompiler.<version>` | the AOT compiler's `THIRD-PARTY-NOTICES.TXT` | the compiler package the publish used, copied at publish |
| `Microsoft.SqlServer.TransactSql.ScriptDom.180.140.0` | `LICENSE` and `NOTICE.txt` | this folder |
| `System.CommandLine.2.0.14` | `LICENSE.txt` | this folder |

The .NET packages carry their licence files, so the publish copies them from the packages it restored
(a target in `src/Haruspex.Cli/Haruspex.Cli.csproj`), and they can't drift from the runtime that was linked.
The compiler's notices are the runtime's, word for word today; both are kept, since they are two packages' notices and
could part.

## The two libraries' texts

ScriptDom's and System.CommandLine's packages name their licence only as an expression (`MIT`) and hold no licence file,
so there is no copyright line to copy from them. Their texts are committed here, taken byte for byte from the upstream
repositories:

- **ScriptDom 180.140.0**: `LICENSE` and `NOTICE.txt` (the notice for the parser generator whose code ScriptDom's parser
  incorporates) from [microsoft/SqlScriptDOM](https://github.com/microsoft/SqlScriptDOM) at commit `3ea69b6fc1ed76b63eca6df868f3d1347b92e9fb`
  (`main` on 22 Nov 2026). The package names commit `9286938f832f746ae50ca5f5a7b6bcc2c89aa465` as its source, which the
  public repository doesn't hold, so the files come from the public `main`. Neither has changed there since before
  180.140.0: `LICENSE` last changed in commit `6384406f` (29 Sep 2022) and `NOTICE.txt` in `84926d40` (24 Apr 2023).
- **System.CommandLine 2.0.14**: `src/command-line-api/LICENSE.md` from [dotnet/dotnet](https://github.com/dotnet/dotnet)
  at commit `0427b9abbead3fa64c6655eb4290c537397131fa`, the commit the package names as its source. It is saved as
  `LICENSE.txt`, so that the prose checks, which read the repository's Markdown, leave Microsoft's text alone.

## When a dependency changes

The test `The_committed_licenses_are_those_of_the_packages_the_command_ships`
(`tests/Haruspex.Tests/Packaging/LicenseTests.cs`) reads the packages the command depends on from its lock file
(`src/Haruspex.Cli/packages.lock.json`) and fails unless this folder has a folder for each, at its locked version, and
none besides. So a new version of ScriptDom or System.CommandLine, or a new package, fails it until its licence is
fetched from upstream again and this page says where from. The .NET folders follow the SDK by themselves.

The pack script (`packaging/npm/pack.mjs`) refuses a binary whose folder doesn't hold `LICENSE` and every one of these
licences, so a platform's npm package can't be made without them, in CI or by hand.
