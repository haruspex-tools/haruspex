# The npm packages are `haruspex` and a scope per platform

**Date:** 17 Nov 2026

**Decision.** The command is the package `haruspex`, unscoped. Each platform's binary is a package in the `@haruspex` scope,
named as npm names the platform: `@haruspex/linux-x64`, `@haruspex/linux-arm64`, `@haruspex/darwin-arm64`,
`@haruspex/darwin-x64` and `@haruspex/win32-x64`, with more added the same way. A preview built from a commit is
`0.0.0-preview-<the commit's first eight hex digits>` in every package. Nothing is published yet; the names become permanent
when one is.

**Context.** Teams install haruspex with npm and run `npx haruspex`, so the command's package needs the tool's own name, and
`haruspex` was free on npm on 7 Nov 2026 and again on 17 Nov 2026, as were `@haruspex/linux-x64` and `haruspex-linux-x64`
(looked up read-only). A scope is an npm organisation, which whoever publishes first creates; whether the `@haruspex`
organisation is free can't be seen without signing in. The binaries come one per platform, as esbuild's and Biome's do
([the packaging README](../../packaging/npm/README.md)).

**Alternatives.**
- *Unscoped platform packages*, `haruspex-linux-x64` and so on. Anyone can publish a name next to them, such as
  `haruspex-linux-x86`, that looks like one of the set; npm's policy against many similar unscoped names is a further risk. A
  scope is one owner's namespace.
- *A scope for everything*, `@haruspex/cli` as the command. `npx @haruspex/cli` is longer to type, and the bare name, left
  free, could be taken by someone else.
- *Biome's form*, `@haruspex/cli-linux-x64`, leaving room for packages in the scope that aren't binaries. The one planned
  (the small runtime that reassembles `FOR JSON`) has a name of its own, which doesn't clash with a platform's.
- *`0.0.0-preview.<commit>`*, with a dot. Semver reads an identifier of digits alone as a number and forbids its leading
  zero, so a commit such as `01234567` would make the version invalid and `npm pack` would refuse it. A hyphen makes the commit
  part of one identifier, valid whatever its digits.

**Reason.** `npx haruspex` is the shortest command, the scope keeps the platform packages under one owner, and the names follow
npm's and esbuild's, so the package for a machine can be worked out from `process.platform` and `process.arch`. The
organisation `haruspex` has to be created on npm before the first publish; if it is taken, the scope changes here, before
anything is published, and nothing else does.

**In the code:** `platforms` in `packaging/npm/haruspex/lib/launcher.js`, and `previewVersion` in `packaging/npm/version.mjs`.
