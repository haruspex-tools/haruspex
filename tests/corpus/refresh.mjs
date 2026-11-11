// Fetches the corpus from Microsoft's sql-server-samples repository at a named commit, and rewrites the vendored
// files and manifest.json, so that an update arrives in the repository as a diff that can be read.
//
// Usage: node tests/corpus/refresh.mjs <commit>    a full SHA, a short one, a branch or a tag
//
// The commit is resolved to its full SHA through GitHub's API, and the list of files is read from that commit's tree.
// Each file is downloaded from raw.githubusercontent.com by the SHA, never by a branch, and checked against the
// tree's own hash of it (git's blob hash), so what is written is exactly what the commit holds, byte for byte. Then
// the vendored folder is emptied and rewritten, and manifest.json lists every file with its SHA-256, which
// CorpusManifestTests checks. Nothing else in the repository is touched: after a refresh, run the corpus test with
// HARUSPEX_ACCEPT=1 to write the new figures, and read both diffs (tests/corpus/README.md).
//
// Needs Node 18 or later (for fetch) and nothing else. GitHub allows 60 API calls an hour without a token, and a
// refresh makes two; the downloads are not API calls.

import { createHash } from "node:crypto";
import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const repository = "microsoft/sql-server-samples";

// What is vendored, as paths in the upstream repository. Everything else in it (about 500 MB) is left out.
const selection = [
  {
    // WideWorldImporters as an SSDT project: the project file, which lists the scripts and the settings they are
    // built with, and every script. Its solution file, .gitignore, README and XML settings file are not SQL or the
    // project, and are left out.
    folder: "samples/databases/wide-world-importers/wwi-ssdt/wwi-ssdt/",
    keep: (path) => path.endsWith(".sql") || path.endsWith(".sqlproj"),
  },
  {
    // AdventureWorks as one install script. The CSV files beside it (about 100 MB) are the data it loads, which
    // a static analysis doesn't read.
    file: "samples/databases/adventure-works/oltp-install-script/instawdb.sql",
  },
  {
    // The repository's licence (MIT), which applies to every file above and has to travel with them.
    file: "license.txt",
  },
];

const here = dirname(fileURLToPath(import.meta.url));
// The vendored files keep their upstream paths below this folder, so each one's origin is its path.
const vendored = join(here, "sql-server-samples");

async function api(path) {
  const response = await fetch(`https://api.github.com/repos/${repository}/${path}`, {
    headers: { Accept: "application/vnd.github+json", "User-Agent": "haruspex-corpus-refresh" },
  });
  if (!response.ok) throw new Error(`GitHub API ${path}: ${response.status} ${response.statusText}`);
  return response.json();
}

function selected(path) {
  return selection.some((s) => (s.file ? path === s.file : path.startsWith(s.folder) && s.keep(path)));
}

/** Git's hash of a blob: SHA-1 over a header and the bytes. The tree lists it, so a download can be checked. */
function gitBlobHash(bytes) {
  return createHash("sha1").update(`blob ${bytes.length}\0`).update(bytes).digest("hex");
}

async function download(sha, entry) {
  const url = `https://raw.githubusercontent.com/${repository}/${sha}/${entry.path}`;
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${url}: ${response.status} ${response.statusText}`);
  const bytes = Buffer.from(await response.arrayBuffer());
  if (gitBlobHash(bytes) !== entry.sha) throw new Error(`${entry.path}: the download doesn't match the commit's tree.`);
  return bytes;
}

async function main(ref) {
  const commit = await api(`commits/${encodeURIComponent(ref)}`);
  const sha = commit.sha;
  const tree = await api(`git/trees/${sha}?recursive=1`);
  // A truncated tree would silently leave files out.
  if (tree.truncated) throw new Error("GitHub truncated the tree listing, so the selection can't be complete.");

  const entries = tree.tree.filter((e) => e.type === "blob" && selected(e.path));
  for (const s of selection.filter((s) => s.file)) {
    if (!entries.some((e) => e.path === s.file)) throw new Error(`${s.file} is not in ${sha}.`);
  }

  // Everything is downloaded and checked before anything is written, so a failed refresh leaves the old files.
  const files = [];
  for (const entry of entries) {
    files.push({ path: entry.path, bytes: await download(sha, entry) });
  }

  rmSync(vendored, { recursive: true, force: true });
  for (const file of files) {
    const target = join(vendored, file.path);
    mkdirSync(dirname(target), { recursive: true });
    writeFileSync(target, file.bytes);
  }

  // Ordinal order, so the manifest doesn't depend on the machine's locale.
  files.sort((a, b) => (a.path < b.path ? -1 : a.path > b.path ? 1 : 0));
  const manifest = {
    repository: `https://github.com/${repository}`,
    commit: sha,
    committed: commit.commit.committer.date,
    licence: { spdx: "MIT", file: "license.txt" },
    folder: "sql-server-samples",
    files: files.map((f) => ({
      path: f.path,
      bytes: f.bytes.length,
      sha256: createHash("sha256").update(f.bytes).digest("hex"),
    })),
  };
  writeFileSync(join(here, "manifest.json"), `${JSON.stringify(manifest, null, 2)}\n`);
  console.log(`Wrote ${files.length} files from ${repository} at ${sha}, and manifest.json.`);
}

if (process.argv.length !== 3) {
  console.error("usage: node tests/corpus/refresh.mjs <commit>");
  process.exit(2);
}
main(process.argv[2]).catch((error) => {
  console.error(error.message);
  process.exit(1);
});
