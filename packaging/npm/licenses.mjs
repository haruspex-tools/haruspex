// The licences that must go with every copy of a binary (packaging/licenses/README.md), and the check that a native
// artifact holds them. The publish puts them beside the binary (HaruspexLicenses in src/Haruspex.Cli/Haruspex.Cli.csproj);
// the pack script checks each artifact here before it packs anything, and copies them into the platform's package.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const repository = path.resolve(here, "..", "..");

/** The licences of the libraries compiled in, committed because their packages hold none: a folder per package. */
export const committed = path.join(repository, "packaging", "licenses");

/** The committed licences, as they must appear in an artifact: `licenses/<id>.<version>/<file>`. */
function committedFiles() {
  const folders = fs.readdirSync(committed, { withFileTypes: true }).filter((entry) => entry.isDirectory());
  return folders.flatMap((folder) =>
    fs
      .readdirSync(path.join(committed, folder.name), { withFileTypes: true })
      .filter((entry) => entry.isFile())
      .map((entry) => `${folder.name}/${entry.name}`),
  );
}

/**
 * The .NET packages whose licence files the publish copies from the packages it restored, by the folder the publish gives
 * each: the package's id, then its version, which follows the SDK and so isn't fixed here.
 */
function dotnet(rid) {
  return [
    {
      what: "the .NET runtime's",
      folder: `Microsoft.NETCore.App.Runtime.NativeAOT.${rid}.<version>`,
      matches: (name) => name.startsWith(`Microsoft.NETCore.App.Runtime.NativeAOT.${rid}.`),
      files: ["LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"],
    },
    {
      // The compiler package for the machine that compiled, which on CI is the platform's own.
      what: "the AOT compiler's",
      folder: "runtime.<rid>.Microsoft.DotNet.ILCompiler.<version>",
      matches: (name) => /^runtime\.[^.]+\.Microsoft\.DotNet\.ILCompiler\./.test(name),
      files: ["THIRD-PARTY-NOTICES.TXT"],
    },
  ];
}

/**
 * What the native artifact in `artifact` (the folder holding the binary, as CI's haruspex-<rid> downloads) lacks of the
 * licences that must go with it: an empty list when it holds them all. Each committed licence must be there byte for byte.
 */
export function missingLicenses(artifact, rid) {
  const missing = [];
  if (!fs.existsSync(path.join(artifact, "LICENSE"))) missing.push("LICENSE, haruspex's own");

  const licenses = path.join(artifact, "licenses");
  const folders = fs.existsSync(licenses)
    ? fs.readdirSync(licenses, { withFileTypes: true }).filter((e) => e.isDirectory()).map((e) => e.name)
    : [];

  for (const { what, folder, matches, files } of dotnet(rid)) {
    const found = folders.filter(matches);
    if (found.length !== 1) {
      missing.push(`licenses/${folder}, ${what} (${found.length === 0 ? "no such folder" : `${found.length} such folders`})`);
      continue;
    }
    for (const file of files) {
      if (!fs.existsSync(path.join(licenses, found[0], file))) missing.push(`licenses/${found[0]}/${file}`);
    }
  }

  for (const file of committedFiles()) {
    const copy = path.join(licenses, ...file.split("/"));
    if (!fs.existsSync(copy)) missing.push(`licenses/${file}`);
    else if (!fs.readFileSync(copy).equals(fs.readFileSync(path.join(committed, ...file.split("/"))))) {
      missing.push(`licenses/${file} as committed in packaging/licenses (the artifact's differs)`);
    }
  }
  return missing;
}

/** Copies an artifact's licences into a platform package's folder: `LICENSE`, and `licenses/` as it is. */
export function copyLicenses(artifact, folder) {
  fs.copyFileSync(path.join(artifact, "LICENSE"), path.join(folder, "LICENSE"));
  fs.cpSync(path.join(artifact, "licenses"), path.join(folder, "licenses"), { recursive: true });
}
