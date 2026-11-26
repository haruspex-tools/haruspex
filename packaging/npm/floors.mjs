// The oldest machine each Linux binary runs on, as packaging/npm/README.md states it, and the check that a binary needs no
// more: the newest glibc symbol version it uses (`objdump -T`), and the x86-64 ISA levels its GNU property note says it needs
// (`readelf -n`), which the loader checks before the program starts. CI's native job runs this file on each Linux binary it
// publishes, before the upload. Both come from the build image as much as from the code: its glibc bounds the newest symbol a
// binary can be linked against, and Ubuntu's x86-64-v3 variant of glibc's startup files (on Ubuntu 26.04's runner) marks every
// binary linked with them as needing x86-64-v3. So a change of image is checked here rather than found by a user.
//
//   node packaging/npm/floors.mjs <binary> <rid>
import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { parseArgs } from "node:util";

/**
 * The floors packaging/npm/README.md documents, by runtime identifier: the newest glibc version a binary may need, and the
 * highest x86-64 ISA level (null on arm64, whose binary carries no x86 level and may name none).
 */
export const floors = {
  "linux-x64": { glibc: "2.34", isa: "x86-64-baseline" },
  "linux-arm64": { glibc: "2.34", isa: null },
};

/** The x86-64 ISA levels, lowest first, as readelf names them. */
const levels = ["x86-64-baseline", "x86-64-v2", "x86-64-v3", "x86-64-v4"];

/** Compares two dotted versions by number: negative when `a` is older. */
function compareVersions(a, b) {
  const x = a.split(".").map(Number);
  const y = b.split(".").map(Number);
  for (let i = 0; i < Math.max(x.length, y.length); i++) {
    const d = (x[i] ?? 0) - (y[i] ?? 0);
    if (d !== 0) return d;
  }
  return 0;
}

/** Every glibc version `objdump -T` names in its listing of a binary's dynamic symbols, newest last. */
export function glibcVersions(symbols) {
  const found = new Set([...symbols.matchAll(/\bGLIBC_(\d+(?:\.\d+)+)\b/g)].map((m) => m[1]));
  return [...found].sort(compareVersions);
}

/**
 * The x86 ISA levels `readelf -n` says a binary needs, as written. Its "used" lines are left out: only the levels a binary
 * needs are checked by the loader.
 */
export function isaNeeded(notes) {
  return [...notes.matchAll(/x86 ISA needed:\s*([^\n]*)/g)].flatMap((m) =>
    m[1]
      .split(",")
      .map((level) => level.trim())
      .filter((level) => level !== ""),
  );
}

/**
 * What is wrong with a binary for its runtime identifier, from what `objdump -T` and `readelf -n` printed for it: an empty
 * list when it needs no newer glibc and no higher ISA level than its floors. A listing that names no glibc version at all, or
 * a level this file doesn't know, is a problem too, so that output it can't read never passes.
 */
export function problems(rid, symbols, notes) {
  const floor = floors[rid];
  if (!floor) return [`no floors are documented for ${rid}`];
  const found = [];

  const versions = glibcVersions(symbols);
  if (versions.length === 0) found.push("objdump -T names no GLIBC_ version, so the floor can't be checked");
  const above = versions.filter((v) => compareVersions(v, floor.glibc) > 0);
  if (above.length > 0) found.push(`needs glibc ${above.join(", ")}, above the documented ${floor.glibc}`);

  for (const level of isaNeeded(notes)) {
    if (floor.isa === null) found.push(`needs ${level}, though a ${rid} binary should name no x86 ISA level`);
    else if (!levels.includes(level)) found.push(`needs ${level}, an ISA level this check doesn't know`);
    else if (levels.indexOf(level) > levels.indexOf(floor.isa)) found.push(`needs ${level}, above the documented ${floor.isa}`);
  }
  return found;
}

/** What `objdump -T` and `readelf -n` print for a binary, as the native job's runner has them. */
function readBinary(binary) {
  return {
    symbols: execFileSync("objdump", ["-T", binary], { encoding: "utf8", maxBuffer: 64 * 1024 * 1024 }),
    notes: execFileSync("readelf", ["-n", binary], { encoding: "utf8" }),
  };
}

/**
 * Checks one binary, as the native job does before its upload: throws, naming each problem, if there is one, and otherwise
 * returns the line to print. `read` gives what objdump and readelf print for it; the tests pass their own.
 */
export function check(binary, rid, read = readBinary) {
  const { symbols, notes } = read(binary);
  const found = problems(rid, symbols, notes);
  if (found.length > 0) throw new Error(`${binary} needs more than packaging/npm/README.md says: ${found.join("; ")}`);
  const isa = isaNeeded(notes);
  return `${binary} needs glibc ${glibcVersions(symbols).at(-1)} at most and ${isa.length > 0 ? isa.join(", ") : "no x86 ISA level"}: within its floors.`;
}

/** The command: exit code 1 when the binary needs more than its floors. */
function main() {
  const { positionals } = parseArgs({ allowPositionals: true });
  if (positionals.length !== 2) throw new Error("give the binary and its runtime identifier");
  const [binary, rid] = positionals;
  console.log(check(binary, rid));
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    main();
  } catch (error) {
    console.error(`floors: ${error.message}`);
    process.exitCode = 1;
  }
}
