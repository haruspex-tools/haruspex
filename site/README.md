# The site at haruspex.tools

The site at `https://haruspex.tools` serves every address haruspex writes into a file it doesn't own: the schema a config file
names in `$schema`, the schema each JSON report names, the IR schemas' `$id`, the pages a diagnostic, a reason and the command's
help link to, and SARIF's `informationUri`. Each is built in the code from one constant, `HaruspexSite`
(`src/Haruspex.Ir/HaruspexSite.cs`), so none of them names the repository's host, and the repository can move without one
changing ([decision 0121](../docs/decisions/0121-every-address-haruspex-writes-is-served-from-haruspex-tools.md)).

## What it serves

| Address | What answers it |
|---|---|
| `/` | A page that leads to the repository. |
| `/schemas/main/haruspex.schema.json`, `/schemas/main/haruspex.output.schema.json` | `schemas/*.json` on `main`: what a preview build names. |
| `/schemas/v1.2.3/haruspex.schema.json`, and the report's | The same files as the tag `v1.2.3` holds them: what that release names. |
| `/schemas/ir/0.30.schema.json` | Each IR schema in `schemas/ir` on `main`, and any a release tag holds that `main` no longer does. |
| `/diagnostics/`, `/diagnostics/HAR2009` | A page that leads to `docs/diagnostics/README.md` or `docs/diagnostics/HAR2009.md` on `main`, on GitHub. |
| `/reasons/`, `/reasons/count-overflow` | A page that leads to `docs/reasons/README.md` or `docs/reasons/count-overflow.md` on `main`. |
| `/docs/`, `/docs/configuration` | A page that leads to `docs/README.md` or `docs/configuration.md` on `main`. |

The schemas are served as files, so an editor that fetches one gets JSON. The pages are Markdown on GitHub, where they are
written and reviewed. The site leads to them rather than rendering them, which needs no renderer and no second copy, and
leaves their addresses free to be served by pages of the site's own later.
GitHub Pages can't answer with a redirect, so each address of a page is a folder whose `index.html` sends the reader on at once
(`<meta http-equiv="refresh">`), names the page as canonical, and links it. Pages answers `/diagnostics/HAR2009` by sending the
reader to `/diagnostics/HAR2009/`, which serves that file.

`SiteTests` holds the site to the code: built from a copy of the repository tagged `v1.2.3`, it serves exactly the addresses
`HaruspexSite` names, each address the command writes in a preview and in a release, each schema as the repository holds it,
and each page leads to a file of the repository. The script's own tests are in `test/`, run by Node's test runner from the
unit suite.

## How it is built and deployed

```sh
node site/build.mjs <checkout> <out>
```

builds the whole site from a clone whose working tree is `main` and which holds the release tags, into a new folder. It runs
offline: it reads the working tree, and each tag through git. A tag is a release's when it is `v` and a version with no
suffix, as the release build requires ([packaging/npm](../packaging/npm/README.md#a-release)); any other is passed over.

The workflow `.github/workflows/site.yml` runs it on every push to `main`, on every tag `v*`, and by hand, and deploys the result
to GitHub Pages from `main` and from tags only. It always checks out `main` with its whole history, whatever started it, so a
tag's run deploys `main`'s site with the tag's schemas in it. A deployment replaces the whole site and nothing is kept from an
earlier one, so the site is always what the repository holds. A lost or broken site is mended by running the workflow again:

```sh
gh workflow run Site --repo haruspex-tools/haruspex --ref main
```

## Setting it up

Once, by the maintainer, in the haruspex-tools organisation and at the domain's DNS:

1. Verify `haruspex.tools` for the organisation (its settings, Pages, "Add a domain"), with the DNS `TXT` record GitHub gives.
   A verified domain can't be taken by another account's Pages site.
2. Point the domain's apex at GitHub Pages: its `A` and `AAAA` records, from GitHub's documentation on the day.
3. In the repository's settings, Pages: source "GitHub Actions", custom domain `haruspex.tools`, and "Enforce HTTPS" once the
   certificate is issued.
4. In the repository's settings, Environments, `github-pages`: allow deployments from tags `v*` as well as from `main`, or a
   release's run will be refused.
