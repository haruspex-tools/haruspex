import { test } from "node:test";
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { buildSite, redirectPage, releaseTags, repository } from "../build.mjs";

// An invented repository, made in a temporary folder: git with no user settings, so that a signing key or a hook of the
// machine's can't change what it holds.
function repo(files) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "haruspex-site-"));
  const none = path.join(root, "no-config");
  const env = { ...process.env, GIT_CONFIG_GLOBAL: none, GIT_CONFIG_SYSTEM: none };
  const git = (...args) =>
    execFileSync("git", ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", ...args], { cwd: path.join(root, "repo"), env, stdio: "pipe" });
  fs.mkdirSync(path.join(root, "repo"));
  git("init", "-q", "-b", "main");
  const put = (contents) => {
    for (const [file, text] of Object.entries(contents)) {
      const full = path.join(root, "repo", ...file.split("/"));
      if (text === null) fs.rmSync(full);
      else {
        fs.mkdirSync(path.dirname(full), { recursive: true });
        fs.writeFileSync(full, text);
      }
    }
    git("add", "-A");
    git("commit", "-q", "-m", "files");
  };
  put(files);
  return { root, checkout: path.join(root, "repo"), out: path.join(root, "site"), git, put };
}

const base = {
  "schemas/haruspex.schema.json": '{"$id":"config at 1.2.3"}\n',
  "schemas/haruspex.output.schema.json": '{"$id":"report at 1.2.3"}\n',
  "schemas/ir/0.29.schema.json": '{"$id":"ir 0.29 at 1.2.3"}\n',
  "schemas/ir/README.md": "# Not a schema\n",
  "docs/README.md": "# Docs\n",
  "docs/configuration.md": "# Configuration\n",
  "docs/diagnostics/README.md": "# Codes\n",
  "docs/diagnostics/HAR2009.md": "# HAR2009\n",
  "docs/reasons/README.md": "# Reasons\n",
  "docs/reasons/count-overflow.md": "# count-overflow\n",
};

const read = (site, file) => fs.readFileSync(path.join(site.out, ...file.split("/")), "utf8");

test("main's schemas, a release tag's own schemas, and every IR schema either holds", (t) => {
  const site = repo(base);
  t.after(() => fs.rmSync(site.root, { recursive: true, force: true }));
  site.git("tag", "v1.2.3");
  site.git("tag", "-a", "-m", "annotated", "v1.3.0");
  site.git("tag", "v2.0.0-rc.1");
  site.git("tag", "not-a-release");
  site.put({
    "schemas/haruspex.schema.json": '{"$id":"config on main"}\n',
    "schemas/ir/0.29.schema.json": '{"$id":"ir 0.29 on main"}\n',
    "schemas/ir/0.30.schema.json": '{"$id":"ir 0.30 on main"}\n',
  });
  // main loses 0.29's schema after the tag: the site still serves the one the release published.
  site.put({ "schemas/ir/0.29.schema.json": null });

  const written = buildSite(site.checkout, site.out);

  assert.deepEqual(written, [
    "diagnostics/HAR2009/index.html",
    "diagnostics/index.html",
    "docs/configuration/index.html",
    "docs/index.html",
    "index.html",
    "reasons/count-overflow/index.html",
    "reasons/index.html",
    "schemas/ir/0.29.schema.json",
    "schemas/ir/0.30.schema.json",
    "schemas/main/haruspex.output.schema.json",
    "schemas/main/haruspex.schema.json",
    "schemas/v1.2.3/haruspex.output.schema.json",
    "schemas/v1.2.3/haruspex.schema.json",
    "schemas/v1.3.0/haruspex.output.schema.json",
    "schemas/v1.3.0/haruspex.schema.json",
  ]);
  assert.equal(read(site, "schemas/main/haruspex.schema.json"), '{"$id":"config on main"}\n');
  assert.equal(read(site, "schemas/v1.2.3/haruspex.schema.json"), '{"$id":"config at 1.2.3"}\n');
  assert.equal(read(site, "schemas/ir/0.29.schema.json"), '{"$id":"ir 0.29 at 1.2.3"}\n');
  assert.equal(read(site, "schemas/ir/0.30.schema.json"), '{"$id":"ir 0.30 on main"}\n');
});

test("main's IR schema is served where a tag holds another of the same version", (t) => {
  const site = repo(base);
  t.after(() => fs.rmSync(site.root, { recursive: true, force: true }));
  site.git("tag", "v1.2.3");
  site.put({ "schemas/ir/0.29.schema.json": '{"$id":"ir 0.29 on main"}\n' });

  buildSite(site.checkout, site.out);

  assert.equal(read(site, "schemas/ir/0.29.schema.json"), '{"$id":"ir 0.29 on main"}\n');
});

test("a tag from before the schemas existed adds nothing", (t) => {
  const site = repo({ "README.md": "# Early\n" });
  t.after(() => fs.rmSync(site.root, { recursive: true, force: true }));
  site.git("tag", "v0.0.1");
  site.put(base);

  const written = buildSite(site.checkout, site.out);

  assert.equal(written.filter((f) => f.startsWith("schemas/v")).length, 0);
});

test("each page leads to its Markdown on main, in the repository", (t) => {
  const site = repo(base);
  t.after(() => fs.rmSync(site.root, { recursive: true, force: true }));

  buildSite(site.checkout, site.out);

  const target = (file) => read(site, file).match(/<meta http-equiv="refresh" content="0; url=([^"]+)">/)[1];
  assert.equal(target("diagnostics/HAR2009/index.html"), `${repository}/blob/main/docs/diagnostics/HAR2009.md`);
  assert.equal(target("diagnostics/index.html"), `${repository}/blob/main/docs/diagnostics/README.md`);
  assert.equal(target("reasons/count-overflow/index.html"), `${repository}/blob/main/docs/reasons/count-overflow.md`);
  assert.equal(target("docs/configuration/index.html"), `${repository}/blob/main/docs/configuration.md`);
  assert.equal(target("index.html"), repository);
  assert.equal(repository, "https://github.com/haruspex-tools/haruspex");
});

test("the site is built only into a new folder", (t) => {
  const site = repo(base);
  t.after(() => fs.rmSync(site.root, { recursive: true, force: true }));
  fs.mkdirSync(site.out);

  assert.throws(() => buildSite(site.checkout, site.out), /already exists/);
});

test("only a tag of a version with no suffix is a release", () => {
  assert.deepEqual(releaseTags(["v1.2.3", "v0.10.0", "1.2.3", "v1.2", "v1.2.3-rc.1", "v01.2.3", "latest", ""]), ["v1.2.3", "v0.10.0"]);
});

test("a redirect page's text is escaped", () => {
  const html = redirectPage('a "b" <c>', "https://example.invalid/?a=1&b=2");
  assert.match(html, /<title>a &quot;b&quot; &lt;c&gt;<\/title>/);
  assert.match(html, /url=https:\/\/example\.invalid\/\?a=1&amp;b=2"/);
});
