# How haruspex is written

This guide covers everything a reader reads: the Markdown in `docs/` and beside the code, XML documentation,
code comments, diagnostics and the generated code's comments. It exists so that text written by many sessions,
and by contributors, reads as one voice.

## Who we write for

Two readers, at once:

- **An experienced developer meeting haruspex for the first time**, who wants to know what it does, whether to
  trust it, and where to look.
- **Someone learning how a static analyser is built**, the project's own maintainers included. The source is literate
  (`docs/internals/`), so an explanation should leave the reader understanding the idea, not only this code.

Write for someone clever who doesn't yet know the subject. Explain an idea in general terms before showing how
haruspex does it, and name it as the literature does.

## Voice

- **Plain and precise.** Short sentences, common words, one idea per sentence where possible. Say exactly what
  is true and stop.
- **No claim stronger than the evidence.** The documentation is held to haruspex's own rule: never claim more
  than is guaranteed. "Typed 32 of 38 fields in AdventureWorks", not "types almost everything". A figure in public text
  comes from [the corpus figures](corpus-figures.md), which a test writes
  ([decision 0020](decisions/0020-public-figures-come-from-public-corpora.md)).
- **No marketing words**: *powerful*, *seamless*, *blazing*, *robust*, *cutting-edge*, *best-in-class*,
  *revolutionary*. No minimisers: *simply*, *just*, *easily*, *obviously*, *of course*. What is easy for the
  writer may not be for the reader.
- **Describe the tool in the third person** ("haruspex infers…"). Address the reader as *you* only in
  instructions ("you can override a field's type"). Avoid *we* except in the decision log, where it means the
  project.
- **Contractions are welcome in explanation** (*doesn't*, *isn't*). Specification text (the IR specification, the
  configuration reference) uses full forms and *must*, *must not*, *may*, in their usual senses.
- **No exclamation marks.**
- **People are *they*** unless their pronouns are known.

## Spelling

- **British English** in prose: *behaviour*, *colour*, *organise*, *recognise*, *licence* (noun) and *license*
  (verb), *analyser*. The repository already writes it this way; the only *-ize* spellings are .NET API names.
- **Identifiers keep their own spelling**: `JsonSerializer`, `Color` in a third-party API, `normalize` in a SQL
  function name. Spell them as they are, in backticks.
- **haruspex is lowercase**, as a name of the tool, everywhere except the start of a .NET namespace or type
  (`Haruspex.Ir`). Reword rather than begin a sentence with it.

## Punctuation

- **No dashes as punctuation.** Use a comma, a colon, parentheses or a new sentence instead of an em or en
  dash. The repository has none today. A hyphen in a compound word (*read-only*) is fine, and an en dash in a
  range is replaced by *to* (*0.8 to 0.15*).
- **No serial comma**: *lengths, ranges and value sets*. The repository writes it this way throughout. Add the
  comma only where a list would otherwise be ambiguous.
- **Write *for example* and *that is***, not *e.g.* and *i.e.*
- **Double quotes** for a phrase quoted or named in prose; backticks, not quotes, for anything that is code.

## Headings

- **Sentence case**: *What it deliberately doesn't do*, not *What It Deliberately Doesn't Do*.
- A heading **names its subject or states its point**. No version numbers, slice names or dates in headings;
  history goes in the decision log or the changelog.

## Words with a fixed meaning

Use these terms consistently, and link the first use on a page to where the term is defined.

| Term | Meaning | Not |
|---|---|---|
| shape | what a result, field or value looks like: its type, nullability and facets | schema (the generated artefact) |
| claim | a statement haruspex makes about a value, which must hold for every execution | assertion, assumption |
| unknown | what haruspex says when it can't prove a shape, always with a reason | any, untyped, error |
| observed | established by running the real engine in a differential test | verified, confirmed |
| facet | a fact about a value beyond its type: a length, range, number of digits or value set | constraint (that is a CHECK constraint) |
| value set | the closed set of values a string or number can take | enum (the generated form) |
| live JSON / JSON text | JSON that embeds as JSON, and a string holding JSON | raw JSON, stringified JSON |
| IR | the intermediate representation, haruspex's output format | AST, model |
| setting, control | a tunable parameter | knob |
| fixture | a database used by the tests, invented (two take their tables from Microsoft's samples, to run the README's examples) | sample, mock |

Name ideas as the literature names them, at least once: *abstract interpretation*, *lattice*, *join*, *least
fixed point*, *widening*, *strongly connected component*, *dataflow analysis*.

## Code in prose

- **Backticks for code**: identifiers, file paths, commands, options, SQL keywords and type names (`FOR JSON`,
  `decimal(38, 2)`, `--include`).
- **SQL keywords in capitals in prose** (`OUTER APPLY`). Examples may be in lower case, consistently within a
  block.
- **Paths are relative to the repository root**, and a link to a file uses a relative link, which a test checks.

## Numbers and dates

- **Numerals for figures**, with a comma for thousands (*2,645 tests*); words for one to nine in ordinary prose
  (*three passes*).
- **Dates as *11 Nov 2026*** in the decision log, the changelog and dated notes. No dates elsewhere unless the
  date explains something.
- **Percentages with %**, and a figure always says what it is a figure of.

## Explanation and decision context

- **Lead with the point**, then the reason, then the detail.
- **Say why beside what.** A rule that isn't obvious says why it holds and names the test that observed it:
  "never null (observed: `Count_star_over_no_rows_is_zero`)".
- **No history as narrative.** A comment or document describes the code as it is. When the past explains a
  choice, give a dated decision ("Decided 10 Nov 2026: COUNT is nullable, because…") and link the decision log;
  never "slice 6g added…" or "this used to…".
- **Don't restate code.** A comment that says what the next line does is noise; one that says why is signal.
- **Lists for three or more parallel items, tables for comparisons, a diagram when a picture is clearer than a
  paragraph.**

## Code comments and XML documentation

- **A type's summary says its one reason to exist**, in one sentence, and links the guide section that explains
  the idea.
- **A member's summary says what it is for**, in the domain's words, not what its name already says.
  `/// <summary>The column the reference reads, or why it can't be bound.</summary>`, not
  `/// <summary>Gets the binding.</summary>`.
- **Remarks carry the why** and the observed test.

## Diagnostics and generated comments

- **A diagnostic says what is wrong, where, and what to do**, in that order: "`sales.get_order`: `@id` is
  compared with `customer.id`, which is `bigint`; declare `@id` as `bigint`." It has a stable code that links
  its page.
- **Generated comments are for the reader of the generated file**: what a check means and why it is there, in
  one line, with shared caveats stated once at the top of the file.

## Diagrams

- **Mermaid**, in the Markdown beside the text it illustrates, validated in CI.
- **One idea per diagram**, a sentence before it saying what to look at, and labels in the same words as the
  text.

## What is checked by a tool, and what by review

| Rule | Checked by |
|---|---|
| British spelling; the project dictionary of terms and identifiers | cspell (`en-GB`), and Vale (`haruspex.Spelling`) for the American and Oxford forms the dictionaries accept |
| *haruspex* in lowercase, and not at the start of a sentence | Vale (`haruspex.ToolName`, `haruspex.ToolNameStart`) |
| Marketing words and minimisers | Vale (`haruspex.Hype`, `haruspex.Minimisers`) |
| *knob* and the other replaced terms in the table above | Vale (`haruspex.Terms`) |
| No dashes as punctuation; *for example*, not *e.g.*; no exclamation marks | Vale (`haruspex.Punctuation`) |
| Sentence-case headings | Vale (`haruspex.Headings`) |
| No slice names, review rounds or citations of the private design document, in headings or prose | Vale (`haruspex.History`), and `tests/prose/check.sh` with the same rule's tokens in SQL |
| Relative links resolve | the Markdown link test (S1) |
| Mermaid diagrams parse | the diagram check in CI (S1b) |
| Lead with the point; say why; don't restate code; claims no stronger than the evidence | review (checklist items 9 and 10) |
