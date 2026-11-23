// The pack script over stand-in binaries: what each package declares, what each tarball holds, and what it refuses.
// npm pack runs for real, so the tarballs are the ones npm would install; nothing is installed or published.
import { test, before, after } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { createRequire } from "node:module";
import { pack } from "../pack.mjs";
import { leastZod, mainManifest, platformManifest } from "../packages.mjs";
import { previewVersion } from "../version.mjs";

const require = createRequire(import.meta.url);
const { platforms } = require("../haruspex/lib/launcher.js");

const commit = "250ced9cf25efd9ec2e16ef8097da6b11961ec92";
const version = "0.0.0-preview-250ced9c";

let root;

/** A folder of stand-in binaries laid out as CI's artifacts download: haruspex-<rid>/<binary>. */
function binaries(name, builtFrom = commit, rids = platforms.map((p) => p.rid)) {
  const folder = path.join(root, name);
  for (const p of platforms.filter((q) => rids.includes(q.rid))) {
    fs.mkdirSync(path.join(folder, `haruspex-${p.rid}`), { recursive: true });
    // The binaries' own version string is the packages' version and +<commit> (InformationalVersion); a dependency's names
    // another commit.
    const bytes = `stand-in for ${p.rid}\0 2.0.14+0427b9abbead3fa64c6655eb4290c537397131fa\0 ${previewVersion(builtFrom)}+${builtFrom}\0`;
    fs.writeFileSync(path.join(folder, `haruspex-${p.rid}`, p.binary), bytes, { mode: 0o644 });
  }
  return folder;
}

let packed;

// npm on Windows can't mark a file executable, so there the script packs only the Windows binary (as it refuses the rest).
const packable = process.platform === "win32" ? platforms.filter((p) => p.os === "win32") : platforms;

before(() => {
  root = fs.mkdtempSync(path.join(os.tmpdir(), "haruspex-pack-"));
  const only = process.platform === "win32" ? packable.map((p) => `${p.os}-${p.cpu}`) : undefined;
  packed = pack({ binaries: binaries("all"), out: path.join(root, "out"), commit, only });
});

after(() => fs.rmSync(root, { recursive: true, force: true }));

test("the main package lists every platform package as an optional dependency at its own version", () => {
  const manifest = mainManifest(version);
  assert.equal(manifest.name, "haruspex");
  assert.equal(manifest.version, version);
  assert.deepEqual(manifest.bin, { haruspex: "bin/haruspex.js" });
  assert.deepEqual(
    manifest.optionalDependencies,
    Object.fromEntries(platforms.map((p) => [p.packageName, version])),
  );
  assert.equal(manifest.license, "Apache-2.0");
});

test("the main package asks for the least zod the module is sound on, and only where the project has zod", () => {
  const manifest = mainManifest(version);
  assert.deepEqual(manifest.peerDependencies, { zod: `>=${leastZod} <5` });
  assert.deepEqual(manifest.peerDependenciesMeta, { zod: { optional: true } });
});

test("a platform package is installed only where its binary runs", () => {
  const linux = platformManifest(platforms.find((p) => p.rid === "linux-x64"), version);
  assert.deepEqual([linux.name, linux.os, linux.cpu, linux.libc], ["@haruspex.tools/linux-x64", ["linux"], ["x64"], ["glibc"]]);
  const windows = platformManifest(platforms.find((p) => p.rid === "win-x64"), version);
  assert.deepEqual([windows.os, windows.cpu, windows.libc], [["win32"], ["x64"], undefined]);
  assert.equal(windows.version, version);
  assert.equal(windows.bin, undefined, "the launcher runs the binary; npm never links it");
});

test("packs one tarball per platform and the main package, named by the version", () => {
  const names = packed.map((p) => path.basename(p.tarball)).sort();
  assert.deepEqual(names, [
    `haruspex-${version}.tgz`,
    ...packable.map((p) => `haruspex.tools-${p.os}-${p.cpu}-${version}.tgz`),
  ].sort());
  for (const p of packed) assert.ok(fs.existsSync(p.tarball), p.tarball);
});

test("the main package holds the launcher, the config schema, the licence and a readme", () => {
  const main = packed.find((p) => p.name === "haruspex");
  assert.deepEqual(main.files.map((f) => f.path).sort(), [
    "LICENSE",
    "README.md",
    "bin/haruspex.js",
    "haruspex.schema.json",
    "lib/launcher.js",
    "package.json",
  ]);
  const schema = fs.readFileSync(new URL("../../../schemas/haruspex.schema.json", import.meta.url), "utf8");
  assert.ok(main.files.find((f) => f.path === "haruspex.schema.json").size === Buffer.byteLength(schema));
});

test("each platform package holds its binary, with the licence and a readme", () => {
  for (const platform of packable) {
    const p = packed.find((q) => q.name === platform.packageName);
    assert.deepEqual(p.files.map((f) => f.path).sort(), ["LICENSE", "README.md", platform.binary, "package.json"].sort());
  }
});

test("a binary for Linux or macOS is packed executable", (t) => {
  if (process.platform === "win32") return t.skip("npm on Windows packs no file executable, so the script packs none there");
  for (const platform of platforms.filter((p) => p.os !== "win32")) {
    // The mode npm reports is the one it wrote into the tarball, which npm keeps when it installs the package.
    const mode = packed.find((q) => q.name === platform.packageName).files.find((f) => f.path === platform.binary).mode;
    assert.equal(mode & 0o111, 0o111, `${platform.binary} in ${platform.packageName} is mode ${mode.toString(8)}`);
  }
});

test("on Windows, refuses to pack a binary for Linux or macOS", () => {
  // npm there writes every file as mode 644, and such a binary would install unable to run.
  assert.throws(
    () => pack({ binaries: binaries("all"), out: path.join(root, "out-windows"), commit, host: "win32" }),
    (error) => /executable/.test(error.message) && error.message.includes("--only win32-x64"),
  );
  assert.equal(fs.existsSync(path.join(root, "out-windows")), false);
});

test("refuses a binary built from another commit, naming it", () => {
  const other = "1111111111111111111111111111111111111111";
  assert.throws(
    () => pack({ binaries: binaries("other", other), out: path.join(root, "out-other"), commit, host: "linux" }),
    (error) => error.message.includes("haruspex-linux-x64") && error.message.includes(commit),
  );
  assert.equal(fs.existsSync(path.join(root, "out-other")), false, "nothing is written when a binary is refused");
});

test("refuses a missing platform unless it is left out by name", () => {
  const some = binaries("some", commit, ["osx-arm64", "win-x64"]);
  assert.throws(
    () => pack({ binaries: some, out: path.join(root, "out-some"), commit, host: "linux" }),
    (error) => error.message.includes("linux-x64") && error.message.includes("--only"),
  );
  const only = pack({ binaries: some, out: path.join(root, "out-only"), commit, only: ["win32-x64"] });
  assert.deepEqual(only.map((p) => p.name).sort(), ["@haruspex.tools/win32-x64", "haruspex"]);
});

test("refuses a platform name it doesn't know", () => {
  assert.throws(
    () => pack({ binaries: binaries("all"), out: path.join(root, "out-x"), commit, only: ["darwin-arm"] }),
    /darwin-arm\b.*darwin-arm64/s,
  );
});
