// The launcher's choice of binary and how it runs it, with the platform, the module resolution and the process injected.
// One test at the end runs the real launcher against a real native binary (a copy of Node itself).
import { test } from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const require = createRequire(import.meta.url);
const launcher = require("../haruspex/lib/launcher.js");

const glibc = "2.39";

test("chooses the package for each platform haruspex ships", () => {
  const cases = [
    [{ platform: "linux", arch: "x64", glibc }, "@haruspex.tools/linux-x64", "haruspex"],
    [{ platform: "linux", arch: "arm64", glibc }, "@haruspex.tools/linux-arm64", "haruspex"],
    [{ platform: "darwin", arch: "arm64" }, "@haruspex.tools/darwin-arm64", "haruspex"],
    [{ platform: "darwin", arch: "x64" }, "@haruspex.tools/darwin-x64", "haruspex"],
    [{ platform: "win32", arch: "x64" }, "@haruspex.tools/win32-x64", "haruspex.exe"],
  ];
  for (const [host, name, binary] of cases) {
    const chosen = launcher.choosePlatform(host);
    assert.equal(chosen.platform?.packageName, name, JSON.stringify(host));
    assert.equal(chosen.platform.binary, binary);
  }
});

test("refuses Linux without glibc, naming musl", () => {
  const chosen = launcher.choosePlatform({ platform: "linux", arch: "x64", glibc: undefined });
  assert.equal(chosen.platform, undefined);
  assert.match(chosen.problem, /musl/);
  assert.match(chosen.problem, /glibc/);
});

test("refuses a platform with no binary, listing those there are", () => {
  for (const host of [{ platform: "win32", arch: "arm64" }, { platform: "freebsd", arch: "x64" }]) {
    const chosen = launcher.choosePlatform(host);
    assert.equal(chosen.platform, undefined);
    assert.match(chosen.problem, new RegExp(`${host.platform} on ${host.arch}`));
    assert.match(chosen.problem, /Windows on x64/);
  }
});

test("every package name follows the platform it is for", () => {
  assert.deepEqual(
    launcher.platforms.map((p) => p.packageName),
    launcher.platforms.map((p) => `@haruspex.tools/${p.os}-${p.cpu}`),
  );
});

/** A host where everything works, each part replaceable by a test. */
function host(overrides = {}) {
  const errors = [];
  const spawned = [];
  return {
    errors,
    spawned,
    platform: "darwin",
    arch: "arm64",
    glibc: undefined,
    ownVersion: "0.0.0-preview-250ced9c",
    args: ["generate", "--out", "src/db"],
    resolve: (request) => `/app/node_modules/${request}`,
    readVersion: () => "0.0.0-preview-250ced9c",
    spawn: (file, args) => {
      spawned.push({ file, args });
      return { status: 0, signal: null };
    },
    writeError: (text) => errors.push(text),
    ...overrides,
  };
}

test("runs the platform's binary with the arguments as given", () => {
  const h = host();
  assert.equal(launcher.launch(h), 0);
  assert.deepEqual(h.spawned, [
    { file: "/app/node_modules/@haruspex.tools/darwin-arm64/haruspex", args: ["generate", "--out", "src/db"] },
  ]);
  assert.deepEqual(h.errors, []);
});

test("exits with the binary's own exit code", () => {
  const h = host({ spawn: () => ({ status: 2, signal: null }) });
  assert.equal(launcher.launch(h), 2);
});

test("exits as a shell does when the binary is killed by a signal", () => {
  const h = host({ spawn: () => ({ status: null, signal: "SIGTERM" }) });
  assert.equal(launcher.launch(h), 128 + os.constants.signals.SIGTERM);
});

const notFound = () => {
  throw Object.assign(new Error("Cannot find module"), { code: "MODULE_NOT_FOUND" });
};

test("says which package is missing and why it may be", () => {
  const h = host({ readVersion: notFound, resolve: notFound });
  assert.equal(launcher.launch(h), 1);
  const text = h.errors.join("");
  assert.match(text, /@haruspex\.tools\/darwin-arm64/);
  assert.match(text, /optional dependencies/);
  assert.equal(h.spawned.length, 0);
});

test("tells a package without its binary from a package that isn't there", () => {
  const h = host({ resolve: notFound });
  assert.equal(launcher.launch(h), 1);
  const text = h.errors.join("");
  assert.match(text, /@haruspex\.tools\/darwin-arm64 is installed/);
  assert.match(text, /no haruspex\b/);
  assert.doesNotMatch(text, /optional dependencies/);
  assert.match(text, /[Ii]nstall .* again/);
  assert.equal(h.spawned.length, 0);
});

test("refuses a platform package of another version", () => {
  const h = host({ readVersion: () => "0.0.0-preview-0123abcd" });
  assert.equal(launcher.launch(h), 1);
  const text = h.errors.join("");
  assert.match(text, /0\.0\.0-preview-0123abcd/);
  assert.match(text, /0\.0\.0-preview-250ced9c/);
  assert.equal(h.spawned.length, 0);
});

test("says why the binary could not be started", () => {
  const h = host({ spawn: () => ({ error: Object.assign(new Error("spawn ENOEXEC"), { code: "ENOEXEC" }) }) });
  assert.equal(launcher.launch(h), 1);
  assert.match(h.errors.join(""), /ENOEXEC/);
});

test("says to install again when the binary has lost its permission to run", () => {
  const h = host({ spawn: () => ({ error: Object.assign(new Error("spawnSync /x EACCES"), { code: "EACCES" }) }) });
  assert.equal(launcher.launch(h), 1);
  const text = h.errors.join("");
  assert.match(text, /EACCES/);
  assert.match(text, /executable/);
  assert.match(text, /[Ii]nstall .* again/);
});

test("refuses an unsupported platform without starting anything", () => {
  const h = host({ platform: "linux", arch: "x64", glibc: undefined });
  assert.equal(launcher.launch(h), 1);
  assert.match(h.errors.join(""), /musl/);
  assert.equal(h.spawned.length, 0);
});

test("the launcher runs a real binary from its platform package", (t) => {
  const chosen = launcher.choosePlatform(launcher.currentHost());
  if (!chosen.platform) return t.skip(`no package for this machine: ${chosen.problem}`);

  // The layout npm installs: the launcher's package and the platform package side by side in node_modules. The binary is
  // Node itself, which shows the launcher hands over its arguments and exit code to a native program unchanged.
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "haruspex-launcher-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const version = "0.0.0-preview-250ced9c";
  const main = path.join(root, "node_modules", "haruspex");
  const platform = path.join(root, "node_modules", ...chosen.platform.packageName.split("/"));
  fs.cpSync(path.join(import.meta.dirname, "..", "haruspex", "bin"), path.join(main, "bin"), { recursive: true });
  fs.cpSync(path.join(import.meta.dirname, "..", "haruspex", "lib"), path.join(main, "lib"), { recursive: true });
  fs.writeFileSync(path.join(main, "package.json"), JSON.stringify({ name: "haruspex", version }));
  fs.mkdirSync(platform, { recursive: true });
  fs.writeFileSync(path.join(platform, "package.json"), JSON.stringify({ name: chosen.platform.packageName, version }));
  // A symbolic link elsewhere, since a Node built against a shared libnode (Homebrew's) finds it beside its real path. On
  // Windows, where a link needs a privilege, a hard link or a copy: Node for Windows is a single file.
  const binary = path.join(platform, chosen.platform.binary);
  if (process.platform !== "win32") fs.symlinkSync(process.execPath, binary);
  else {
    try {
      fs.linkSync(process.execPath, binary);
    } catch {
      fs.copyFileSync(process.execPath, binary);
    }
  }

  const script = "console.log(JSON.stringify(process.argv.slice(1))); process.exit(7)";
  const result = spawnSync(process.execPath, [path.join(main, "bin", "haruspex.js"), "-e", script, "a b", "--c"], {
    encoding: "utf8",
  });
  assert.equal(result.stderr, "");
  assert.deepEqual(JSON.parse(result.stdout), ["a b", "--c"]);
  assert.equal(result.status, 7);
});
