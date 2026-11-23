# Prose checks

The house style for everything a reader reads is [docs/style.md](../../docs/style.md). Its last table says which
rules a tool checks. This folder runs those tools: `check.sh` runs all of them, locally and in CI.

```sh
tests/prose/check.sh                 # spelling, style and diagrams
tests/prose/check.sh spelling        # or any one of them
```

| Check | Tool | Reads | Configuration |
|---|---|---|---|
| Spelling, in British English | cspell | Markdown, C# (comments, strings and identifiers), workflows | `cspell.config.yaml`, `.cspell/words.txt` |
| The house style, and British spelling in prose | Vale | Markdown, C# comments | `.vale.ini`, `.vale/styles/haruspex/` |
| No private history (slice names, review rounds, the plan's notes) in the files Vale doesn't read, and in test names | Perl, with `haruspex.History`'s own tokens | SQL, scripts, workflows and project files; the names of C# methods, their underscores read as spaces | `.vale/styles/haruspex/History.yml` |
| Mermaid diagrams parse | Mermaid, in Node | Markdown | `diagrams.mjs` |

Each check runs first on the files in `fixtures`, which break its rules on purpose, and compares what it reports
with the `.expected` file beside them (for the diagrams, `diagrams.mjs --self-test`). So a rule that has stopped
matching anything fails the check rather than passing it in silence. The fixtures are left out of the real run.

## When a check fails

Fix the text. When the text is right and the tool is wrong:

- **A word cspell doesn't know** that is a real term here (a T-SQL function, a tool's name, a word of the
  subject) goes in `.cspell/words.txt`, in its section. A word that is right in one test only (a column
  abbreviation in a query, a name misspelt on purpose to test a message) goes in `cspell.config.yaml`, under
  that file, with the reason. Never add a misspelling to the dictionary.
- **An identifier with American spelling** keeps it (docs/style.md, "Spelling"). Vale already skips it (code
  and identifiers with a capital inside are not prose to it); if cspell refuses it, add its name to
  `ignoreRegExpList` in `cspell.config.yaml`.
- **A Vale rule that is wrong for a file** is turned off for that file in `.vale.ini`, with the reason. A rule
  that is wrong everywhere is changed in its `.yml`, and its fixture with it.

## Why these tools

- **cspell**, because it reads code as well as prose: it splits identifiers (`IrJsonWriter` is checked as three
  words) and knows C#'s keywords. It runs with the `en-GB` dictionary, which refuses most American spellings
  but accepts the Oxford ones in *-ize*; the software dictionaries cspell adds accept more, in C# strings too. So
  `cspell.config.yaml` forbids the American and Oxford forms of the verbs a technical text uses, which is what
  checks the diagnostics in C# strings, and British spelling in prose is also a Vale rule, `haruspex.Spelling`: a pattern for every *-ize* and *-yze* word (bar *size*,
  *prize* and a few that are British too) and a list of the common American forms
  that no pattern catches. A pattern, not a list, so a word nobody thought of is caught too.
- **Vale**, because its rules are small YAML files that state the guide's rules directly, and because it reads
  the comments in C# (`//`, `/* */` and `///`) and skips code, in C# and in Markdown. It is one Go binary; CI
  runs its Docker image, pinned by digest, with no network. Locally, `check.sh` uses `vale` from the `PATH` if
  it is the pinned version (or the binary named in `VALE`), and the same image otherwise.
- **Mermaid's own parser**, the code that draws diagrams on GitHub, so a diagram that passes here is one GitHub
  can read. It runs in Node; jsdom supplies the DOM that some diagram types need for one step (DOMPurify
  sanitises class diagrams' labels). A headless browser would also lay the diagram out, at the cost of a
  Chromium download, and layout is not the question asked.

The npm packages are pinned in `package-lock.json` and installed with `npm ci --ignore-scripts`. Each tool's
version was the newest released at least two weeks before it was pinned.
