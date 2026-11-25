// Builds the site served at haruspex.tools from a checkout of the repository: every address haruspex writes into a file it
// doesn't own (HaruspexSite in src/Haruspex.Ir) and nothing else. Why the site exists and why it is laid out so:
// docs/decisions/0121-every-address-haruspex-writes-is-served-from-haruspex-tools.md; how it is deployed: site/README.md.
//
//   node site/build.mjs <checkout> <out>
//
// <checkout> is a clone whose working tree is main and which holds the release tags (v1.2.3). Each run builds the whole site
// from it, main and every tag, since a deployment to GitHub Pages replaces the whole site: nothing is kept from an earlier one.
// It runs offline, reading only the working tree and the clone's own tags through git.
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

/** Where the pages the site leads to are kept: the repository, whose own address may change without any of the site's. */
export const repository = "https://github.com/haruspex-tools/haruspex";

/** A release's tag: `v` and a version with no suffix, as the build requires of a release (packaging/npm/README.md, "A release"). */
const releaseTag = /^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;

/** The tags among `names` that mark a release, in the order given. */
export function releaseTags(names) {
  return names.filter((name) => releaseTag.test(name));
}

/** The release tags among `names`, newest version first (`v1.10.0` before `v1.2.3`). */
function newestFirst(names) {
  const parts = (tag) => tag.slice(1).split(".").map(Number);
  return releaseTags(names).sort((a, b) => {
    const [x, y] = [parts(a), parts(b)];
    return y[0] - x[0] || y[1] - x[1] || y[2] - x[2];
  });
}

/**
 * A page that sends its reader on to `target`: a refresh at once, the canonical link for search engines, and the link itself for
 * a reader whose browser does neither. GitHub Pages can't answer with a redirect of its own, so the page does it.
 */
export function redirectPage(title, target) {
  const href = escape(target);
  return (
    "<!doctype html>\n" +
    '<html lang="en">\n' +
    '<meta charset="utf-8">\n' +
    `<title>${escape(title)}</title>\n` +
    `<link rel="canonical" href="${href}">\n` +
    `<meta http-equiv="refresh" content="0; url=${href}">\n` +
    `<p>This page is at <a href="${href}">${href}</a>.</p>\n` +
    "</html>\n"
  );
}

function escape(text) {
  return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

/**
 * Writes the site of `checkout` into `out`, which must not exist yet, and returns the paths it wrote, relative to `out` and
 * sorted. The layout (each address with the file that answers it):
 *
 * - `/` (`index.html`): leads to the repository.
 * - `/schemas/main/<file>.json`: the config file's and the report's schemas, `schemas/*.json` in the working tree.
 * - `/schemas/v1.2.3/<file>.json`: the same schemas as each release's tag holds them.
 * - `/schemas/ir/<version>.schema.json`: every IR schema main or any tag holds; main's where both do, else the newest tag's.
 * - `/diagnostics/`, `/diagnostics/HAR2009`: lead to `docs/diagnostics/README.md` and `docs/diagnostics/HAR2009.md` on main.
 * - `/reasons/`, `/reasons/count-overflow`: lead to `docs/reasons/README.md` and `docs/reasons/count-overflow.md` on main.
 * - `/docs/`, `/docs/configuration`: lead to `docs/README.md` and `docs/configuration.md` on main.
 *
 * A page that main no longer holds but a release does leads to the page at the newest release that holds it, as an IR schema
 * main dropped is still served: a release writes these addresses into its users' files, so none may stop answering.
 *
 * A shallow clone is refused: it may lack the tags, and a site built without them would take every release's schemas offline.
 * A page is a folder's `index.html`, so that its address needs no extension: GitHub Pages answers `/diagnostics/HAR2009` by
 * sending the reader to `/diagnostics/HAR2009/`, which serves it.
 */
export function buildSite(checkout, out) {
  if (fs.existsSync(out)) throw new Error(`${out} already exists: the site is built into a new folder, so nothing old is kept`);
  if (git(checkout, ["rev-parse", "--is-shallow-repository"]).trim() !== "false")
    throw new Error(`${checkout} is a shallow clone, which may not hold every release tag: clone it whole (fetch-depth: 0)`);
  const tags = newestFirst(git(checkout, ["tag", "--list"]).split("\n"));
  const written = [];
  const write = (relative, contents) => {
    const file = path.join(out, ...relative.split("/"));
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, contents);
    written.push(relative);
  };
  const page = (address, title, target) => write(`${address}index.html`, redirectPage(title, target));

  page("", "haruspex", repository);

  for (const file of jsonFiles(checkout, "schemas")) write(`schemas/main/${file}`, fs.readFileSync(path.join(checkout, "schemas", file)));

  const irSchemas = new Map(jsonFiles(checkout, "schemas/ir").map((file) => [file, fs.readFileSync(path.join(checkout, "schemas", "ir", file))]));
  for (const tag of tags) {
    for (const file of tagFiles(checkout, tag, "schemas", ".json")) write(`schemas/${tag}/${file}`, gitBytes(checkout, ["show", `${tag}:schemas/${file}`]));
    for (const file of tagFiles(checkout, tag, "schemas/ir", ".json"))
      if (!irSchemas.has(file)) irSchemas.set(file, gitBytes(checkout, ["show", `${tag}:schemas/ir/${file}`]));
  }
  for (const [file, bytes] of irSchemas) write(`schemas/ir/${file}`, bytes);

  for (const [folder, address, title] of [
    ["docs/diagnostics", "diagnostics/", (name) => `haruspex: ${name}`],
    ["docs/reasons", "reasons/", (name) => `haruspex: why ${name}`],
    ["docs", "docs/", (name) => `haruspex: ${name}`],
  ]) {
    const pages = new Map(markdownPages(checkout, folder).map((name) => [name, "main"]));
    for (const tag of tags)
      for (const file of tagFiles(checkout, tag, folder, ".md")) if (!pages.has(file.slice(0, -3))) pages.set(file.slice(0, -3), tag);
    for (const [name, at] of pages) {
      const target = `${repository}/blob/${at}/${folder}/${name}.md`;
      if (name === "README") page(address, `haruspex: ${folder}`, target);
      else page(`${address}${name}/`, title(name), target);
    }
  }

  return written.sort();
}

/** The `.json` files directly in `folder` of the working tree, sorted. */
function jsonFiles(checkout, folder) {
  return fs.readdirSync(path.join(checkout, ...folder.split("/")), { withFileTypes: true })
    .filter((e) => e.isFile() && e.name.endsWith(".json"))
    .map((e) => e.name)
    .sort();
}

/** The names of the Markdown pages directly in `folder` of the working tree, without `.md`, sorted. */
function markdownPages(checkout, folder) {
  return fs.readdirSync(path.join(checkout, ...folder.split("/")), { withFileTypes: true })
    .filter((e) => e.isFile() && e.name.endsWith(".md"))
    .map((e) => e.name.slice(0, -".md".length))
    .sort();
}

/**
 * The files ending in `extension` directly in `folder` at `tag`, sorted; none when the tag has no such folder. `ls-tree` with a
 * path reads every tree from the tag's root down, lists nothing for a folder that isn't there, and fails for a tree it can't read
 * (the root's included), which is thrown: a release is never left out of the site without a word. (`rev-parse tag:folder` can't
 * tell a lost root tree from a missing folder.)
 */
function tagFiles(checkout, tag, folder, extension) {
  return git(checkout, ["ls-tree", "-z", tag, "--", `${folder}/`]).split("\0")
    .filter((entry) => entry !== "")
    .map((entry) => entry.split("\t"))
    .filter(([info, name]) => info.split(" ")[1] === "blob" && name.endsWith(extension))
    .map(([, name]) => name.slice(folder.length + 1))
    .sort();
}

function git(checkout, args) {
  return execFileSync("git", ["-C", checkout, ...args], { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });
}

function gitBytes(checkout, args) {
  return execFileSync("git", ["-C", checkout, ...args], { stdio: ["ignore", "pipe", "pipe"], maxBuffer: 64 * 1024 * 1024 });
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [checkout, out] = process.argv.slice(2);
  if (!checkout || !out) {
    console.error("usage: node site/build.mjs <checkout> <out>");
    process.exit(2);
  }
  const written = buildSite(path.resolve(checkout), path.resolve(out));
  console.log(`Wrote ${written.length} files to ${out}.`);
}
