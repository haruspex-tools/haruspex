// Checks that every Mermaid diagram in the Markdown files named on the command line parses, so that a broken
// diagram fails CI rather than showing an error box on GitHub.
//
// It uses Mermaid's own parser, the code that renders diagrams on GitHub, without a browser. Mermaid parses in
// Node except for one step: some diagram types (class diagrams, for example) sanitise their labels with
// DOMPurify, which needs a DOM. jsdom provides one. A headless browser would also lay the diagram out, which
// costs a Chromium download and adds nothing to the question asked here, which is whether the text parses.
//
// Usage: node diagrams.mjs <file.md>...  Exit code 0 if every diagram parses, 1 if any doesn't, 2 on misuse.
//        node diagrams.mjs --self-test    Checks that fixtures/diagram-valid.md passes and that
//                                         fixtures/diagram-broken.md fails, so the check is known to work.

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { JSDOM } from "jsdom";

const { window } = new JSDOM("");
globalThis.window = window;
globalThis.document = window.document;
const { default: mermaid } = await import("mermaid");

/** The Mermaid blocks in a Markdown file: each one's text and the line its fence opens on. */
function diagrams(markdown) {
  const found = [];
  const lines = markdown.split("\n");
  for (let i = 0; i < lines.length; i++) {
    // A fence opens with three or more backticks or tildes, indented by at most three spaces (CommonMark).
    const open = /^ {0,3}(`{3,}|~{3,})\s*mermaid\s*$/.exec(lines[i]);
    if (!open) continue;
    // It closes with at least as many of the same character, and nothing else on the line.
    const close = new RegExp(`^ {0,3}\\${open[1][0]}{${open[1].length},}\\s*$`);
    const body = [];
    let j = i + 1;
    while (j < lines.length && !close.test(lines[j])) {
      body.push(lines[j]);
      j++;
    }
    found.push({ line: i + 1, text: body.join("\n") });
    i = j;
  }
  return found;
}

/** The problems in one file, each as "file:line: message"; empty if every diagram parses. */
async function check(file) {
  const problems = [];
  for (const diagram of diagrams(readFileSync(file, "utf8"))) {
    try {
      await mermaid.parse(diagram.text);
    } catch (error) {
      const message = String(error?.message ?? error).split("\n").filter(Boolean).join(" ");
      problems.push(`${file}:${diagram.line}: the Mermaid diagram does not parse: ${message}`);
    }
  }
  return problems;
}

const args = process.argv.slice(2);
if (args.length === 0) {
  console.error("usage: node diagrams.mjs <file.md>... | --self-test");
  process.exit(2);
}

if (args[0] === "--self-test") {
  const fixture = (name) => fileURLToPath(new URL(`fixtures/${name}`, import.meta.url));
  const valid = await check(fixture("diagram-valid.md"));
  const broken = await check(fixture("diagram-broken.md"));
  const found = diagrams(readFileSync(fixture("diagram-valid.md"), "utf8")).length;
  const ok = found === 2 && valid.length === 0 && broken.length === 1;
  if (found !== 2) console.error(`self-test: diagram-valid.md has two diagrams, and ${found} were found.`);
  for (const problem of valid) console.error(`self-test: a valid diagram was refused: ${problem}`);
  if (broken.length !== 1) console.error(`self-test: diagram-broken.md should give one problem, and gave ${broken.length}.`);
  if (ok) console.log("diagrams: self-test passed (a valid diagram parses, a broken one is reported).");
  process.exit(ok ? 0 : 1);
}

const problems = (await Promise.all(args.map(check))).flat();
for (const problem of problems) console.error(problem);
const count = args.reduce((n, file) => n + diagrams(readFileSync(file, "utf8")).length, 0);
console.log(`diagrams: ${count} in ${args.length} files, ${problems.length} that don't parse.`);
process.exit(problems.length === 0 ? 0 : 1);
