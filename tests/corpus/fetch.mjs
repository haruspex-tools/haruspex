// Fetches the corpora listed in tests/corpus/fetched.json into a folder outside the checkout, checking every file against
// the hash the manifest records, or pins a corpus to a new commit by rewriting its entry. tests/corpus/README.md says why
// these corpora are fetched rather than vendored, and how the tests use them.
//
// Usage:
//   node tests/corpus/fetch.mjs [id ...]        fetch every corpus (or those given) that isn't already there and whole
//   node tests/corpus/fetch.mjs pin <id> <ref>    move a corpus to a commit, branch or tag, and rewrite its entry
//
// The folder is HARUSPEX_CORPORA if set, otherwise ~/.cache/haruspex/corpora; each corpus is a folder in it, given by its id in
// the manifest, holding its files at their upstream paths. The tests look in the same place.
//
// How a corpus is fetched: git fetches the one commit, with no history (--depth 1) and no file contents at first
// (--filter=blob:none), then a sparse checkout brings only the paths the corpus takes, so a repository of hundreds of
// megabytes costs a few. Each file is read from git's object store (git cat-file), not from the working tree, so it is
// the commit's bytes exactly, whatever the upstream's .gitattributes or this machine's line-ending settings would do to a
// checkout. Only regular files are taken: a symbolic link is refused, since it could point anywhere on this machine.
// Everything is fetched and checked in a temporary folder, and moved into place only when every hash matches, so a
// failed fetch leaves what was there.
//
// Fetched code is untrusted data. Nothing here runs it: git runs no hook from a fetched repository, and this script
// only reads bytes and hashes them.
//
// Needs Node 18 or later and git 2.25 or later.

import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from "node:fs";
import { homedir } from "node:os";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const manifestPath = join(here, "fetched.json");

/** Where the corpora are kept: the same rule as the tests' (FetchedCorpora.Root). */
function corporaRoot() {
  return process.env.HARUSPEX_CORPORA || join(homedir(), ".cache", "haruspex", "corpora");
}

function readManifest() {
  return JSON.parse(readFileSync(manifestPath, "utf8"));
}

function writeManifest(manifest) {
  writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
}

function git(args, options = {}) {
  const result = spawnSync("git", args, {
    maxBuffer: 1 << 30,
    ...options,
    env: { ...process.env, GIT_TERMINAL_PROMPT: "0", GIT_CONFIG_NOSYSTEM: "1" },
  });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(`git ${args.join(" ")} failed: ${result.stderr.toString().trim()}`);
  }
  return result.stdout;
}

/** Whether an upstream path is one the corpus takes: a file named, or a script or project below a folder named. */
function taken(corpus, path) {
  return corpus.take.some((t) => (t.endsWith("/") ? path.startsWith(t) && /\.(sql|sqlproj)$/i.test(path) : path === t));
}

/**
 * Fetches one commit's files that the corpus takes into a new temporary folder below the corpora folder, returning
 * the folder, the commit's date, and each file's path and bytes in ordinal order. Nothing is checked against the
 * manifest here.
 */
function fetchCommit(corpus, commit) {
  mkdirSync(corporaRoot(), { recursive: true });
  const work = mkdtempSync(join(corporaRoot(), `.${corpus.id}.`));
  const repo = join(work, "git");
  mkdirSync(repo);
  const g = (args, options) => git(["-C", repo, ...args], options);
  g(["init", "-q"]);
  g(["config", "core.autocrlf", "false"]);
  g(["config", "core.symlinks", "false"]);
  g(["remote", "add", "origin", corpus.repository]);
  g(["sparse-checkout", "set", "--no-cone", ...corpus.take.map((t) => `/${t}`)]);
  g(["fetch", "-q", "--depth", "1", "--filter=blob:none", "origin", commit]);
  // The checkout fetches the sparse paths' contents in one batch; the files themselves are read from the object store.
  g(["-c", "advice.detachedHead=false", "checkout", "-q", "FETCH_HEAD"]);
  const sha = g(["rev-parse", "FETCH_HEAD"]).toString().trim();
  const committed = g(["log", "-1", "--format=%cI", "FETCH_HEAD"]).toString().trim();

  const entries = g(["ls-tree", "-r", "-z", "FETCH_HEAD"])
    .toString()
    .split("\0")
    .filter((line) => line.length > 0)
    .map((line) => {
      const [meta, path] = line.split("\t");
      const [mode, type, blob] = meta.split(" ");
      return { mode, type, blob, path };
    })
    .filter((e) => e.type === "blob" && taken(corpus, e.path));
  const links = entries.filter((e) => e.mode === "120000");
  if (links.length > 0) throw new Error(`${corpus.id}: refusing symbolic links: ${links.map((e) => e.path).join(", ")}`);
  for (const t of corpus.take.filter((t) => !t.endsWith("/"))) {
    if (!entries.some((e) => e.path === t)) throw new Error(`${corpus.id}: ${t} is not in ${sha}.`);
  }

  const files = [];
  if (entries.length > 0) {
    const out = g(["cat-file", "--batch"], { input: entries.map((e) => `${e.blob}\n`).join("") });
    let at = 0;
    for (const entry of entries) {
      const header = out.subarray(at, out.indexOf(10, at)).toString();
      const [blob, type, size] = header.split(" ");
      if (blob !== entry.blob || type !== "blob") throw new Error(`${corpus.id}: git gave ${header} for ${entry.path}.`);
      const start = out.indexOf(10, at) + 1;
      files.push({ path: entry.path, bytes: Buffer.from(out.subarray(start, start + Number(size))) });
      at = start + Number(size) + 1;
    }
  }
  files.sort((a, b) => (a.path < b.path ? -1 : a.path > b.path ? 1 : 0));
  return { work, sha, committed, files };
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

/** The files below a folder, as forward-slash paths relative to it, in ordinal order. */
function filesBelow(folder) {
  const found = [];
  const walk = (dir) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const full = join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else found.push(relative(folder, full).split(sep).join("/"));
    }
  };
  walk(folder);
  return found.sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
}

/** What is wrong with a corpus's folder against its manifest entry, or nothing when it is whole. */
function problems(corpus, folder) {
  if (!existsSync(folder)) return ["not fetched"];
  const listed = new Map(corpus.files.map((f) => [f.path, f]));
  const wrong = [];
  for (const path of filesBelow(folder)) {
    const file = listed.get(path);
    if (!file) wrong.push(`${path}: not in the manifest`);
    else if (statSync(join(folder, path)).size !== file.bytes || sha256(readFileSync(join(folder, path))) !== file.sha256)
      wrong.push(`${path}: differs from the manifest`);
    listed.delete(path);
  }
  for (const path of listed.keys()) wrong.push(`${path}: missing`);
  return wrong;
}

function writeFiles(folder, files) {
  for (const file of files) {
    const target = join(folder, ...file.path.split("/"));
    mkdirSync(dirname(target), { recursive: true });
    writeFileSync(target, file.bytes);
  }
}

/** Puts a checked folder in place of the corpus's folder, removing the temporary work. */
function install(name, work, files) {
  const staged = join(work, "files");
  writeFiles(staged, files);
  const folder = join(corporaRoot(), name);
  rmSync(folder, { recursive: true, force: true });
  renameSync(staged, folder);
  rmSync(work, { recursive: true, force: true });
}

function fetchCorpus(corpus) {
  const folder = join(corporaRoot(), corpus.id);
  if (problems(corpus, folder).length === 0) {
    console.log(`${corpus.id}: already fetched, ${corpus.files.length} files at ${corpus.commit.slice(0, 12)}.`);
    return;
  }
  const { work, files } = fetchCommit(corpus, corpus.commit);
  try {
    const expected = corpus.files.map((f) => `${f.path} ${f.bytes} ${f.sha256}`).join("\n");
    const actual = files.map((f) => `${f.path} ${f.bytes.length} ${sha256(f.bytes)}`).join("\n");
    if (expected !== actual) {
      throw new Error(`${corpus.id}: what ${corpus.commit} holds doesn't match tests/corpus/fetched.json. Pin it again to see why.`);
    }
    install(corpus.id, work, files);
  } catch (error) {
    rmSync(work, { recursive: true, force: true });
    throw error;
  }
  console.log(`${corpus.id}: fetched ${files.length} files at ${corpus.commit.slice(0, 12)}, every hash checked.`);
}

function resolve(repository, ref) {
  if (/^[0-9a-f]{40}$/.test(ref)) return ref;
  const lines = git(["ls-remote", repository, ref, `refs/tags/${ref}^{}`]).toString().trim().split("\n").filter(Boolean);
  // A tag's own object is not a commit; the peeled line (^{}) is, and wins when there is one.
  const peeled = lines.find((l) => l.endsWith("^{}"));
  const line = peeled ?? lines[0];
  if (!line) throw new Error(`${repository} has no ${ref}.`);
  return line.split("\t")[0];
}

function pin(id, ref) {
  const manifest = readManifest();
  const corpus = manifest.corpora.find((c) => c.id === id);
  if (!corpus) throw new Error(`No corpus with the id ${id} in tests/corpus/fetched.json.`);
  const { work, sha, committed, files } = fetchCommit(corpus, resolve(corpus.repository, ref));
  try {
    corpus.commit = sha;
    corpus.committed = committed;
    corpus.files = files.map((f) => ({ path: f.path, bytes: f.bytes.length, sha256: sha256(f.bytes) }));
    install(id, work, files);
  } catch (error) {
    rmSync(work, { recursive: true, force: true });
    throw error;
  }
  writeManifest(manifest);
  console.log(`${id}: pinned to ${sha}, ${files.length} files; rewrote tests/corpus/fetched.json.`);
}

function main(args) {
  if (args[0] === "pin") {
    if (args.length !== 3) throw new Error("usage: node tests/corpus/fetch.mjs pin <id> <ref>");
    pin(args[1], args[2]);
    return;
  }
  const manifest = readManifest();
  const unknown = args.filter((a) => !manifest.corpora.some((c) => c.id === a));
  if (unknown.length > 0) throw new Error(`No corpus with the id ${unknown.join(", ")} in tests/corpus/fetched.json.`);
  for (const corpus of manifest.corpora.filter((c) => args.length === 0 || args.includes(c.id))) fetchCorpus(corpus);
}

try {
  main(process.argv.slice(2));
} catch (error) {
  console.error(error.message);
  process.exit(1);
}
