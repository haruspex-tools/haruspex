# The npm packages

The haruspex command is a .NET program, compiled ahead of time to one native binary per platform (Native AOT, in CI's native job). Its
users are mostly TypeScript teams, who already have Node and npm, so its main way in is npm: `npm install haruspex`, then
`npx haruspex generate` or a package script, with no .NET runtime and nothing to compile. This folder builds those packages.
Nothing is published yet: the script below makes tarballs, which npm installs from the file.

## The layout

One small package, `haruspex`, holds the command, and one package per platform holds that platform's binary:

| Package | Holds | Installed |
|---|---|---|
| `haruspex` | `bin/haruspex.js` (the command), `lib/launcher.js`, the JSON Schema of `haruspex.json`, the licence | always |
| `@haruspex/linux-x64`, `@haruspex/linux-arm64` | `haruspex`, linked against glibc | on Linux with glibc, on that CPU |
| `@haruspex/darwin-arm64`, `@haruspex/darwin-x64` | `haruspex` | on macOS, on that CPU |
| `@haruspex/win32-x64` | `haruspex.exe` | on Windows on x64 |

`haruspex` lists every platform package in `optionalDependencies`, at its own version exactly. Each platform package sets
`os`, `cpu` and, on Linux, `libc`, so npm installs the one that matches the machine and skips the others; an optional
dependency that doesn't match is not an error. The command (`bin/haruspex.js`) is a few lines of JavaScript: the launcher
works out which platform package belongs to this machine, finds its binary with Node's own module resolution, checks that it
is the same version as itself, and runs it with the arguments as given, its standard streams inherited and its exit code
passed back. `lib/launcher.js` holds the table of platforms, which the pack script reads too, so there is one list.

The Linux binaries need glibc 2.34 or later (the newest symbol version the x64 binary uses): Ubuntu 22.04, Debian 12 and RHEL 9
have it, and an older distribution fails when the binary starts. There is no binary for musl (Alpine) or for Windows on arm64
yet; on those the launcher says so and lists the platforms there are.

### What it borrows

The layout is esbuild's and Biome's, which solved this first:

- **From both:** a main package whose optional dependencies are the platform packages, each limited by `os` and `cpu`, at
  the main package's exact version, and named by npm's own names for the platform (`process.platform`, `process.arch`).
- **From esbuild:** the scope with a package per platform named `@scope/<os>-<cpu>`, and `preferUnplugged` on each, so Yarn's
  Plug'n'Play leaves the binary on disk where it can run; the check that the binary's package is the launcher's version; and
  an error that says why a platform package can be missing (optional dependencies left out, or `node_modules` copied from
  another platform).
- **From Biome:** no install script. The command is a JavaScript launcher that runs the binary on each call, so installing
  runs nothing, which `--ignore-scripts` and pnpm's default would otherwise skip. Its exit code for a binary killed by a signal
  is 128 plus the signal's number, as a shell's is. And the config file's JSON Schema is in the main package, so `$schema`
  resolves in `node_modules`.
- **Not borrowed:** esbuild's install script that copies the binary over the launcher to save starting Node, its WebAssembly
  fallback, and both tools' variable that names a binary to run instead. A build runs haruspex once, so starting Node costs
  little, and a binary from elsewhere can be run directly.
- **Different:** `libc: ["glibc"]` on the Linux packages, where Biome's are split by glibc and musl and esbuild's binaries are
  static. The haruspex binaries are linked against glibc, so npm 10.4 and later skip them under musl, and the launcher reads the C
  library from `process.report` as well, for an older npm that doesn't.

## The version

A preview's packages share one version, `0.0.0-preview-<commit>`, the first eight hex digits of the commit the binaries were
built from: `0.0.0-preview-250ced9c`. The pack script checks that commit against each binary, whose informational version
(`--version`) ends in `+<commit>`, so a tarball can't say it holds a commit it doesn't. 0.0.0 sorts below any release. Releases
will carry haruspex's own version.

## Making the tarballs

To make them, download the native artifacts of a CI run into a folder (the `gh` CLI lays them out as the script expects, a
folder per platform), then:

```sh
gh run download <run id> --pattern 'haruspex-*-*' --dir binaries      # the native artifacts, not haruspex-npm
node packaging/npm/pack.mjs --binaries binaries --out artifacts/npm --commit <the run's commit>
```

`--commit` defaults to the commit checked out. Without every platform's binary the script refuses, unless `--only` names the
platforms to pack, as npm names them (`--only darwin-arm64,win32-x64`). The main package lists every platform whichever are
packed. It needs Node and npm, and nothing else (Node 20.11 or later for its tests). `node --test packaging/npm/test/` runs
its tests, which `NpmPackagingTests` runs with the rest.

## Names and publishing

The names are [decision 0061](../../docs/decisions/0061-the-npm-packages-are-haruspex-and-a-scope-per-platform.md). Before the
first publish, the binaries' third-party notices must be added to the platform packages: a Native AOT binary contains the .NET
runtime and haruspex's libraries (ScriptDom and System.CommandLine, under the MIT licence), and the MIT licence asks that
their notices go with every copy.
