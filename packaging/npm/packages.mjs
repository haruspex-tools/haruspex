// What each npm package declares and holds. The platforms come from the launcher's table, the one list of them.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
export const { platforms } = require("./haruspex/lib/launcher.js");

const here = path.dirname(fileURLToPath(import.meta.url));
const repository = path.resolve(here, "..", "..");

const common = {
  license: "Apache-2.0",
  repository: { type: "git", url: "git+https://github.com/sharpblue/haruspex.git" },
  homepage: "https://github.com/sharpblue/haruspex#readme",
};

/** The `haruspex` package: the launcher, which runs the binary from whichever platform package npm installed. */
export function mainManifest(version) {
  return {
    name: "haruspex",
    version,
    description:
      "Carries a SQL Server database's types into TypeScript by static analysis: Zod schemas, JSON Schema and a function " +
      "for each stored procedure, inferred from the database project's source.",
    keywords: ["sql-server", "t-sql", "zod", "typescript", "codegen", "static-analysis"],
    ...common,
    type: "commonjs",
    bin: { haruspex: "bin/haruspex.js" },
    // The launcher uses nothing newer than Node 16 (`node:` modules, `?.`, `??`), and npm only warns on this; 18 is the
    // floor esbuild states too. The tests and CI run it on Node 24 and later.
    engines: { node: ">=18" },
    // Exact versions: the launcher refuses a binary of any other version.
    optionalDependencies: Object.fromEntries(platforms.map((p) => [p.packageName, version])),
  };
}

/** A platform's package: its binary alone, installed only where `os`, `cpu` and (on Linux) `libc` match. */
export function platformManifest(platform, version) {
  return {
    name: platform.packageName,
    version,
    description: `The haruspex binary for ${platform.label}. Installed by the haruspex package; not for use on its own.`,
    ...common,
    os: [platform.os],
    cpu: [platform.cpu],
    ...(platform.libc ? { libc: [platform.libc] } : {}),
    // Yarn's Plug'n'Play keeps packages zipped; a binary has to be a file on disk to run, as esbuild's packages say too.
    preferUnplugged: true,
  };
}

function writeJson(file, value) {
  fs.writeFileSync(file, `${JSON.stringify(value, null, 2)}\n`);
}

/** Lays out the main package in an empty folder, ready for `npm pack`. */
export function stageMain(folder, version) {
  const source = path.join(here, "haruspex");
  fs.cpSync(path.join(source, "bin"), path.join(folder, "bin"), { recursive: true });
  fs.cpSync(path.join(source, "lib"), path.join(folder, "lib"), { recursive: true });
  fs.copyFileSync(path.join(source, "README.md"), path.join(folder, "README.md"));
  fs.copyFileSync(path.join(repository, "LICENSE"), path.join(folder, "LICENSE"));
  // So that a haruspex.json can name its schema in node_modules and an editor finds it with no network.
  fs.copyFileSync(path.join(repository, "schemas", "haruspex.schema.json"), path.join(folder, "haruspex.schema.json"));
  writeJson(path.join(folder, "package.json"), mainManifest(version));
}

/** Lays out a platform's package in an empty folder, with its binary made executable. */
export function stagePlatform(folder, platform, version, binary) {
  const target = path.join(folder, platform.binary);
  fs.copyFileSync(binary, target);
  // A CI artifact loses the executable bit; npm keeps the bit a file has when it packs it (and on Windows sets it).
  fs.chmodSync(target, 0o755);
  fs.copyFileSync(path.join(repository, "LICENSE"), path.join(folder, "LICENSE"));
  fs.writeFileSync(
    path.join(folder, "README.md"),
    `# ${platform.packageName}\n\nThe [haruspex](https://github.com/sharpblue/haruspex) binary for ${platform.label}. ` +
      "Install `haruspex`, which installs this package where it runs; it is not for use on its own.\n",
  );
  writeJson(path.join(folder, "package.json"), platformManifest(platform, version));
}
