// Turns haruspex's native binaries into npm tarballs, with `npm pack`, without publishing anything. CI runs it after the
// native jobs; packaging/npm/README.md says how to run it locally.
//
//   node packaging/npm/pack.mjs --binaries <folder> [--out artifacts/npm] [--commit <sha>] [--only darwin-arm64,...]
//
// <folder> holds a folder per platform named as CI's artifacts are, haruspex-<rid>, with the binary in it and the licences
// that go with it (packaging/licenses/README.md).
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { parseArgs } from "node:util";
import { missingLicenses } from "./licenses.mjs";
import { platforms, stageMain, stagePlatform } from "./packages.mjs";
import { builtFrom, previewVersion } from "./version.mjs";

const npmName = (p) => `${p.os}-${p.cpu}`;

/** The platforms to pack: all of them, or those named by npm's name for them (`darwin-arm64`). */
function choose(only) {
  if (!only) return platforms;
  const unknown = only.filter((name) => !platforms.some((p) => npmName(p) === name));
  if (unknown.length > 0) {
    throw new Error(`no platform called ${unknown.join(", ")}; the platforms are ${platforms.map(npmName).join(", ")}`);
  }
  return platforms.filter((p) => only.includes(npmName(p)));
}

/** Runs `npm pack` in a staged package's folder and returns npm's report of the tarball it wrote there. */
function npmPack(folder) {
  // npm is a script on Windows (npm.cmd), which only a shell can start, so there it is given as one command line; its words
  // are constants, so nothing needs quoting.
  const command = ["npm", "pack", "--json", "--ignore-scripts"];
  const options = { cwd: folder, encoding: "utf8" };
  const result =
    process.platform === "win32"
      ? spawnSync(command.join(" "), { ...options, shell: true })
      : spawnSync(command[0], command.slice(1), options);
  if (result.status !== 0) throw new Error(`npm pack failed in ${folder}:\n${result.stderr}${result.error ?? ""}`);
  const [report] = JSON.parse(result.stdout);
  return report;
}

/**
 * Packs the main package and one package per platform, at the version the commit gives, into `out`. Every binary must be
 * there and built from that commit, or nothing is written. Returns, for each package, its name, its tarball's path and
 * the files npm put in it. `host` is the platform the script runs on, given by the tests.
 */
export function pack({ binaries, out, commit, only, host = process.platform }) {
  const version = previewVersion(commit);
  const chosen = choose(only);

  // npm on Windows writes every file into a tarball as mode 644, so a binary for Linux or macOS packed there would install
  // without the executable bit and could not run.
  const needExecutable = chosen.filter((p) => p.os !== "win32");
  if (host === "win32" && needExecutable.length > 0) {
    throw new Error(
      `npm on Windows can't mark a file executable, so ${needExecutable.map(npmName).join(", ")} would not run once ` +
        "installed. Pack them on Linux or macOS, as CI does, or pack the Windows binary alone with --only win32-x64.",
    );
  }

  const found = chosen.map((p) => ({ platform: p, binary: path.join(binaries, `haruspex-${p.rid}`, p.binary) }));
  const missing = found.filter((f) => !fs.existsSync(f.binary));
  if (missing.length > 0) {
    throw new Error(
      `no binary at ${missing.map((m) => m.binary).join(", ")}. Give the folder that holds every platform's, or name ` +
        `the platforms to pack with --only (${platforms.map(npmName).join(",")}).`,
    );
  }
  const stale = found.filter((f) => !builtFrom(fs.readFileSync(f.binary), commit));
  if (stale.length > 0) {
    throw new Error(
      `${stale.map((s) => s.binary).join(", ")} ${stale.length === 1 ? "was" : "were"} not built from ${commit}, so ` +
        "the packages' version would not say what they hold. Give the commit the binaries were built from with --commit.",
    );
  }

  // A binary is a copy of the .NET runtime and of the libraries compiled into it, whose licences ask that their notices go
  // with every copy; so a platform's package is never made without them.
  const unlicensed = found
    .map((f) => ({ ...f, missing: missingLicenses(path.dirname(f.binary), f.platform.rid) }))
    .filter((f) => f.missing.length > 0);
  if (unlicensed.length > 0) {
    throw new Error(
      "a binary must travel with the licences of what it contains, and these artifacts lack some:\n" +
        unlicensed.map((u) => `  ${path.dirname(u.binary)}: ${u.missing.join("; ")}`).join("\n") +
        "\nThe publish puts them beside the binary (HaruspexLicenses in src/Haruspex.Cli/Haruspex.Cli.csproj).",
    );
  }

  const staging = fs.mkdtempSync(path.join(os.tmpdir(), "haruspex-npm-"));
  try {
    const packages = [{ name: "haruspex", stage: (folder) => stageMain(folder, version) }];
    for (const { platform, binary } of found) {
      packages.push({ name: platform.packageName, stage: (folder) => stagePlatform(folder, platform, version, binary) });
    }
    const packed = packages.map(({ name, stage }, i) => {
      const folder = path.join(staging, String(i));
      fs.mkdirSync(folder);
      stage(folder);
      const report = npmPack(folder);
      return { name, built: path.join(folder, report.filename), files: report.files };
    });

    // Only once every package has packed, so a failure leaves no partial set behind.
    fs.mkdirSync(out, { recursive: true });
    return packed.map(({ name, built, files }) => {
      const tarball = path.join(out, path.basename(built));
      fs.copyFileSync(built, tarball);
      return { name, tarball, files };
    });
  } finally {
    fs.rmSync(staging, { recursive: true, force: true });
  }
}

function main() {
  const { values } = parseArgs({
    options: {
      binaries: { type: "string" },
      out: { type: "string", default: path.join("artifacts", "npm") },
      commit: { type: "string" },
      only: { type: "string" },
    },
  });
  if (!values.binaries) throw new Error("give the folder of binaries with --binaries <folder>");
  const commit = values.commit ?? head();
  const packed = pack({
    binaries: values.binaries,
    out: values.out,
    commit,
    only: values.only?.split(",").map((s) => s.trim()),
  });
  for (const p of packed) console.log(p.tarball);
}

/** The commit checked out, when none is given: the one CI built the binaries from. */
function head() {
  const result = spawnSync("git", ["rev-parse", "HEAD"], { encoding: "utf8" });
  if (result.status !== 0) throw new Error(`git rev-parse HEAD failed; give the commit with --commit\n${result.stderr}`);
  return result.stdout.trim();
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    main();
  } catch (error) {
    console.error(`pack: ${error.message}`);
    process.exitCode = 1;
  }
}
