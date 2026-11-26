import { test } from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { check, floors, glibcVersions, isaNeeded, problems } from "../floors.mjs";

const require = createRequire(import.meta.url);
const { platforms } = require("../haruspex/lib/launcher.js");

// What objdump -T prints for a binary's dynamic symbols, in the two forms binutils has used (the version bare, or in
// parentheses), trimmed to a few lines.
const symbols = (...versions) =>
  [
    "publish/haruspex:     file format elf64-x86-64",
    "",
    "DYNAMIC SYMBOL TABLE:",
    ...versions.map((v, i) => `0000000000000000      DF *UND*\t0000000000000000 ${i % 2 ? `(GLIBC_${v})` : ` GLIBC_${v} `} symbol${i}`),
    "0000000000000000  w   D  *UND*\t0000000000000000  Base        __gmon_start__",
  ].join("\n");

// What readelf -n prints for a binary's GNU property note, with the ISA levels it needs.
const notes = (needed) =>
  [
    "Displaying notes found in: .note.gnu.property",
    "  Owner                Data size \tDescription",
    "  GNU                  0x00000030\tNT_GNU_PROPERTY_TYPE_0",
    "      Properties: x86 feature used: x86",
    `\tx86 ISA needed: ${needed}`,
    "\tx86 ISA used: x86-64-baseline, x86-64-v2, x86-64-v3, x86-64-v4",
  ].join("\n");

test("the newest glibc version is found in either form objdump writes, by number rather than as text", () => {
  assert.deepEqual(glibcVersions(symbols("2.2.5", "2.34", "2.14", "2.3.4", "2.34")), ["2.2.5", "2.3.4", "2.14", "2.34"]);
});

test("a binary within its floors passes", () => {
  assert.deepEqual(problems("linux-x64", symbols("2.2.5", "2.34"), notes("x86-64-baseline")), []);
  assert.deepEqual(problems("linux-arm64", symbols("2.17", "2.34"), "Displaying notes found in: .note.gnu.build-id"), []);
  assert.deepEqual(problems("linux-x64", symbols("2.34"), ""), [], "a binary with no ISA note needs no level");
});

test("a glibc version above the floor fails, though it sorts below it as text", () => {
  assert.deepEqual(problems("linux-x64", symbols("2.2.5", "2.38"), notes("x86-64-baseline")), [
    "needs glibc 2.38, above the documented 2.34",
  ]);
  assert.deepEqual(problems("linux-arm64", symbols("2.34", "2.100"), ""), ["needs glibc 2.100, above the documented 2.34"]);
});

test("an ISA level above the floor fails, as the note Ubuntu 26.04's x86-64-v3 startup files leave does", () => {
  assert.deepEqual(problems("linux-x64", symbols("2.34"), notes("x86-64-baseline, x86-64-v2, x86-64-v3")), [
    "needs x86-64-v2, above the documented x86-64-baseline",
    "needs x86-64-v3, above the documented x86-64-baseline",
  ]);
});

test("the used levels are not what is needed", () => {
  assert.deepEqual(isaNeeded(notes("x86-64-baseline")), ["x86-64-baseline"]);
});

test("output that can't be read fails rather than passes", () => {
  assert.deepEqual(problems("linux-x64", "", ""), ["objdump -T names no GLIBC_ version, so the floor can't be checked"]);
  assert.deepEqual(problems("linux-x64", symbols("2.34"), notes("x86-64-v5")), [
    "needs x86-64-v5, an ISA level this check doesn't know",
  ]);
  assert.deepEqual(problems("linux-arm64", symbols("2.34"), notes("x86-64-baseline")), [
    "needs x86-64-baseline, though a linux-arm64 binary should name no x86 ISA level",
  ]);
  assert.deepEqual(problems("linux-musl-x64", symbols("2.34"), ""), ["no floors are documented for linux-musl-x64"]);
});

test("every Linux platform the packages ship has its floors, and no other platform has any", () => {
  const linux = platforms.filter((p) => p.os === "linux").map((p) => p.rid);
  assert.deepEqual(Object.keys(floors).sort(), linux.sort());
});

test("the check the native job runs refuses a binary above its floors, naming why, and passes one within them", () => {
  const above = () => ({ symbols: symbols("2.34", "2.38"), notes: notes("x86-64-baseline, x86-64-v2, x86-64-v3") });
  assert.throws(() => check("publish/haruspex", "linux-x64", above), {
    message:
      "publish/haruspex needs more than packaging/npm/README.md says: needs glibc 2.38, above the documented 2.34; " +
      "needs x86-64-v2, above the documented x86-64-baseline; needs x86-64-v3, above the documented x86-64-baseline",
  });
  const within = () => ({ symbols: symbols("2.2.5", "2.34"), notes: notes("x86-64-baseline") });
  assert.equal(
    check("publish/haruspex", "linux-x64", within),
    "publish/haruspex needs glibc 2.34 at most and x86-64-baseline: within its floors.",
  );
});
