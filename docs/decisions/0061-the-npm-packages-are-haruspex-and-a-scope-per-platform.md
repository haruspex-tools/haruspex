# The npm packages are `haruspex` and a scope per platform

**Date:** 17 Nov 2026

**Decision.** The command is the package `haruspex`, unscoped. Each platform's binary is a package in the `@haruspex.tools`
scope, named as npm names the platform: `@haruspex.tools/linux-x64`, `@haruspex.tools/linux-arm64`,
`@haruspex.tools/darwin-arm64`, `@haruspex.tools/darwin-x64` and `@haruspex.tools/win32-x64`, with more added the same way. A
preview built from a commit is `0.0.0-preview-<the commit's first eight hex digits>` in every package. Nothing is published
yet; the names become permanent when one is. The maintainer decided the scope on 17 Nov 2026.

**Context.** Teams install haruspex with npm and run `npx haruspex`, so the command's package needs the tool's own name.
`haruspex` was free on npm on 7 Nov 2026 and again on 17 Nov 2026 (looked up read-only), and the maintainer is reserving it. The
binaries come one per platform, as esbuild's and Biome's do ([the packaging README](../../packaging/npm/README.md)), and a
scope is an npm organisation. The organisation `haruspex` is taken: npm's users and organisations share one namespace, and
the name belongs to an account. So the scope is `haruspex.tools`, the organisation the maintainer created, which matches the
project's domain, haruspex.tools.

**Alternatives.**
- *`@haruspex/<platform>`*, esbuild's form with the tool's bare name. The first choice, and not available.
- *Unscoped platform packages*, `haruspex-linux-x64` and so on. Anyone can publish a name next to them, such as
  `haruspex-linux-x86`, that looks like one of the set; npm's policy against many similar unscoped names is a further risk. A
  scope is one owner's namespace.
- *A scope for everything*, `@haruspex.tools/cli` as the command. `npx @haruspex.tools/cli` is longer to type, and the bare
  name, left free, could be taken by someone else.
- *Biome's form*, `@haruspex.tools/cli-linux-x64`, leaving room for packages in the scope that aren't binaries. The one
  planned (the small runtime that reassembles `FOR JSON`) has a name of its own, which doesn't clash with a platform's.
- *`0.0.0-preview.<commit>`*, with a dot. Semver reads an identifier of digits alone as a number and forbids its leading
  zero, so a commit such as `01234567` would make the version invalid and `npm pack` would refuse it. A hyphen makes the commit
  part of one identifier, valid whatever its digits.

**Reason.** `npx haruspex` is the shortest command, the scope keeps the platform packages under one owner, and the names after
it follow npm's and esbuild's, so the package for a machine can be worked out from `process.platform` and `process.arch`.
A scope named for the domain says who publishes it. Its packages are installed by `haruspex`, never typed by a user, so the
longer scope costs nothing. npm names a scoped package's tarball with the scope and a hyphen, so
`@haruspex.tools/darwin-arm64` packs as `haruspex.tools-darwin-arm64-<version>.tgz`.

**In the code:** `platforms` in `packaging/npm/haruspex/lib/launcher.js`, and `previewVersion` in `packaging/npm/version.mjs`.
