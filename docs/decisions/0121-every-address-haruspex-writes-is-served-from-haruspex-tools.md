# Every address haruspex writes is served from haruspex.tools

**Date:** 24 Nov 2026

**Decision.** Every address haruspex writes into a file it doesn't own is on `https://haruspex.tools`, a domain the maintainer
owns: the schema `haruspex init` names in a config file's `$schema`, the schema each JSON report names, the IR schemas' `$id`,
the pages diagnostics, reasons and the command's help link to, and SARIF's `informationUri`. The host is one constant,
`HaruspexSite.Host`, and each address is built from it. Repository metadata (the repository's address in `Directory.Build.props`, the npm packages' `repository` and
`homepage`, the READMEs' links) names the repository itself, at `github.com/haruspex-tools/haruspex`, since that is what it
describes.

The site is GitHub Pages, deployed by a workflow from `main` and from release tags. Each run builds the whole site from the
repository (`site/build.mjs`): `main`'s schemas at `/schemas/main/`, each release tag's at `/schemas/v1.2.3/`, every IR schema at
`/schemas/ir/<version>.schema.json`, and a page for each diagnostic, reason and documentation page that leads to its Markdown on
`main`, on GitHub. An address a release wrote never stops answering while the repository keeps the release's tag: an IR schema or
a page that `main` has dropped is served from the newest release that holds it. Nothing is kept from one deployment to the next,
so the site is always what the repository holds, and a lost one is built again by running the workflow. A shallow clone, which
may lack the tags, is refused rather than built into a site without the releases.

A schema's new `$id` doesn't move the IR's version. Under [decision 0024](0024-an-ir-reader-reads-only-versions-whose-documents-mean-what-its-own-do.md)
the version moves when the format does: a property or a value added, or what one means changed. The `$id` is where the schema
is found, not part of any document, and no IR document names it. The kept schema of an older version (`schemas/ir/0.29.schema.json`)
takes the new `$id` too, as the one change to it since its version was current, because the site serves it there.

**Context.** Until now each of these addresses named `github.com/sharpblue/haruspex`, so each depended on where the repository
happened to live. The repository moved to the haruspex-tools organisation on 24 Nov 2026. GitHub redirects a moved repository's
old addresses, but only until the old account holds a repository of that name again: one careless fork into it would end the
redirect, and an address in a user's config file would then lead to that fork's files, or to nothing. No release has been made, so only
preview builds have written the old addresses. After a release, every user's config file would hold one.

**Alternatives.**
- *Stay on GitHub and rely on the redirect.* Nothing to build, but every address written would rest on a redirect that one fork into
  the old account ends, and would still name an account the repository has left.
- *Move the addresses to `github.com/haruspex-tools/haruspex`.* Right today, but the addresses would still name the host, so the
  next move would change them again, in files haruspex has already written.
- *Render the pages on the site.* The address is the same either way, so this can come later without any address changing. A page
  that leads to the Markdown needs no renderer and no second copy, and the Markdown stays where it is written and reviewed.
- *Keep each deployment and add to it.* A release's schemas would be published once and kept, but a lost site could not be built
  again, and what the site holds would depend on its history. Building everything from `main` and the tags each time gives the
  same site from the same repository.
- *Serve the schemas from raw.githubusercontent.com at a tag.* That is GitHub's address again, and it serves JSON as plain text.

**Reason.** An address in someone else's file should last as long as haruspex does. A domain the maintainer owns is the one host
that no move of the repository, and no other account, can take away, and serving it from the repository keeps one source for
both the files and their addresses.

**Limits.** GitHub Pages can't redirect, so a page's address answers with a page that sends the reader on, not with an HTTP
redirect. Each address of a page is a folder, and Pages adds its trailing slash with a redirect of its own.

**In the code:** `HaruspexSite` in `src/Haruspex.Ir`, from which `SchemaAddress`, `IrJsonSchema.Id`, `IrReasons.Pages`,
`DiagnosticPages`, `Init` and the help build their addresses; `site/build.mjs` and `.github/workflows/site.yml`; `AddressTests` and
`SiteTests`.
