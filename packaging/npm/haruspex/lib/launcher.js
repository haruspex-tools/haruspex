"use strict";
// Finds the haruspex binary for this machine and runs it. The binary comes in a package of its own for each platform,
// which the haruspex package lists as optional dependencies, each with `os`, `cpu` and, on Linux, `libc` set, so npm
// installs only the one that matches. packaging/npm/README.md explains the layout and what it borrows from esbuild and
// Biome. Everything the launcher reads from its host is passed in, so the tests can stand in for any platform.

const os = require("node:os");

/**
 * The platforms there is a binary for: npm's names for them (`process.platform` and `process.arch`), the .NET runtime
 * identifier the binary is compiled for, and the binary's file name. The pack script reads this table too, so it is the
 * one list of platforms.
 */
const platforms = [
  { os: "linux", cpu: "x64", libc: "glibc", rid: "linux-x64", binary: "haruspex", label: "Linux on x64 (glibc)" },
  { os: "linux", cpu: "arm64", libc: "glibc", rid: "linux-arm64", binary: "haruspex", label: "Linux on arm64 (glibc)" },
  { os: "darwin", cpu: "arm64", rid: "osx-arm64", binary: "haruspex", label: "macOS on Apple silicon" },
  { os: "darwin", cpu: "x64", rid: "osx-x64", binary: "haruspex", label: "macOS on Intel" },
  { os: "win32", cpu: "x64", rid: "win-x64", binary: "haruspex.exe", label: "Windows on x64" },
].map((p) => ({ ...p, packageName: `@haruspex/${p.os}-${p.cpu}` }));

const supported = platforms.map((p) => p.label).join(", ");

/**
 * The platform package for a host, or why there is none. `glibc` is the C library's version on Linux, absent where the
 * C library is another, such as musl on Alpine: the Linux binaries are linked against glibc and don't start without it.
 */
function choosePlatform({ platform, arch, glibc }) {
  if (platform === "linux" && glibc === undefined) {
    return {
      problem:
        "haruspex has no binary for Linux with a C library other than glibc, such as musl on Alpine. " +
        `It has binaries for ${supported}.`,
    };
  }
  const match = platforms.find((p) => p.os === platform && p.cpu === arch);
  if (!match) return { problem: `haruspex has no binary for ${platform} on ${arch}. It has binaries for ${supported}.` };
  return { platform: match };
}

/** What the launcher needs to know about the machine it runs on. */
function currentHost() {
  // Node reports the glibc version it runs with, and none under musl; the same check Node's own tools use.
  const header = process.platform === "linux" ? process.report.getReport().header : {};
  return { platform: process.platform, arch: process.arch, glibc: header.glibcVersionRuntime };
}

/**
 * Runs the binary for the host with the arguments given, and returns the exit code to leave with. Every way it can fail
 * before the binary starts is written to standard error, saying what to do, with exit code 1.
 */
function launch(host) {
  const fail = (text) => {
    host.writeError(`haruspex: ${text}\n`);
    return 1;
  };

  const chosen = choosePlatform(host);
  if (!chosen.platform) return fail(chosen.problem);
  const { packageName, binary } = chosen.platform;

  let file;
  let version;
  try {
    file = host.resolve(`${packageName}/${binary}`);
    version = host.readVersion(packageName);
  } catch (error) {
    if (error?.code !== "MODULE_NOT_FOUND") throw error;
    return fail(
      `the package ${packageName}, which holds the binary for this machine, is not installed. npm installs it as one ` +
        "of haruspex's optional dependencies, so it is missing when optional dependencies were left out " +
        "(--omit=optional or --no-optional), or when node_modules was installed on another platform and copied here. " +
        "Install again on this machine, with optional dependencies.",
    );
  }

  // The launcher and the binary are released together; a binary of another version would be run under this one's name.
  if (version !== host.ownVersion) {
    return fail(
      `${packageName} is version ${version}, and haruspex is version ${host.ownVersion}; they must be the same. ` +
        "Install haruspex again so that npm installs the matching binary.",
    );
  }

  const result = host.spawn(file, host.args);
  if (result.error) return fail(`could not start ${file}: ${result.error.message}`);
  // A process killed by a signal has no status; a shell reports 128 plus the signal's number, and so does this.
  if (result.signal) return 128 + (os.constants.signals[result.signal] ?? 0);
  return result.status;
}

/** The launcher as the `haruspex` command runs it. */
function main() {
  const { spawnSync } = require("node:child_process");
  process.exitCode = launch({
    ...currentHost(),
    ownVersion: require("../package.json").version,
    args: process.argv.slice(2),
    resolve: (request) => require.resolve(request),
    readVersion: (packageName) => require(`${packageName}/package.json`).version,
    // No shell, so arguments reach the binary exactly as given; standard streams are the binary's own.
    spawn: (file, args) => spawnSync(file, args, { stdio: "inherit", shell: false }),
    writeError: (text) => process.stderr.write(text),
  });
}

module.exports = { platforms, choosePlatform, currentHost, launch, main };
