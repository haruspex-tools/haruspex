# A column's description reaches the fields that are that column

**Date:** 26 Nov 2026

**Decision.** The descriptions a database keeps in extended properties are read, and each is written onto the output that is
exactly the thing described: a field that is one column, unchanged; a procedure's parameter; a procedure.

*What is read.* From a package, each `SqlExtendedProperty` element, whose value the package holds as a SQL literal. From a
script, each `EXEC sp_addextendedproperty` (or `sys.sp_addextendedproperty`, in any case and bracketing) whose arguments are
given by position, by name, or by position and then by name, as the server takes them (observed). An argument is read when it
is a string literal, an identifier (the server takes `[Person]` as the string `N'Person'`, as the AdventureWorks script passes
its level names) or `NULL`. A property is a description when its name is one of the config file's `descriptions`, compared
ignoring case; `["MS_Description"]` by default. The names are compared ignoring case because the server compares them in the
database's collation (observed: `MS_Description` and `ms_description` are two properties of one table in a `_BIN2` database,
and one in a case-insensitive one), and a folder of scripts doesn't say which, so one target with both is set twice (below).

*Which levels.* A table, a view and a procedure (`SCHEMA`, then `TABLE`, `VIEW` or `PROCEDURE`), a column of a table or view
(`COLUMN`), and a parameter of a procedure (`PARAMETER`, its name with its `@`, as the server requires: observed). The model
holds each. Every other level (the database, a schema, an index, a constraint, a trigger, a function, a type) describes
nothing an output has, so it is passed over without a word, as the package reader passed over every extended property until
now. A function is one of those: haruspex types procedures and query files, and each generated function in the module is a
procedure's or a query file's. A SQL function reaches the output only as the value it returns inside a procedure's result,
and its description says what the function does, not what that value is, so writing it there would describe the wrong thing.
Its parameters are never sent by a caller, so theirs reach nothing either. When the IR comes to type functions as objects,
their descriptions are read the same way as a procedure's. The level types are compared ignoring case, as the server compares them (observed), and the level names are found as
the model finds any name.

*What is passed over, with a note (HAR1024).* A call through a database (`master.sys.sp_addextendedproperty`), which runs in
that database (observed: through `master`, from inside another database, it fails with 15135), and a script doesn't say which
database it is deployed as; a description whose name, value or level arguments aren't literals, whose value
isn't text (the server takes `42`, an `int`), or whose level names name nothing the source defines (the server refuses it,
15135: observed); and a target given a description twice, by two calls, two names that differ only in case or two packages
(the server refuses the second call, 15233: observed), since which one stands isn't known. A call whose name is a literal
that isn't one of `descriptions` is no description, and is passed over without a word whatever its other arguments.
`sp_updateextendedproperty` and `sp_dropextendedproperty` of a description are passed over with the same note, and the target
they name keeps no description: what stands depends on the order the scripts run, which a folder doesn't say. A `NULL` value
is no description.

*Where it is written.* A result's field takes the description of the column it is, unchanged: a bare reference to a table's
column (no `COLLATE`, no expression around it), or that column through `*`, or a column of a view, derived table or CTE that
is itself such a column, where the view's own column description, if it has one, replaces the table's. A field read from
two columns or from an expression over one takes none. A column of a set operation or of a recursive CTE, and a field where
two equal shapes merge (two paths' rows alike, a combined document), keeps a description only where every branch gives the
same, since its value is any branch's; and a union that joining makes
keeps none in any variant, since a value of one variant may match another (an owner's name short enough to pass as a dog's). A procedure's parameter takes its
own. A procedure takes its own, written in the comment above each of its declarations. A table's and a view's own description
reach no output, since no output is a table; the model holds them for a later use.

In the Zod module a description is the property's `.describe("…")`, outermost, so that `z.toJSONSchema` and an editor's
hover show it, and the first lines of the comment above the property. In JSON Schema the whole description, as the database
gives it, starts the `description`; where haruspex has notes for that schema (its SQL type, why it may be null), they follow
after a blank line, so a reader can tell the database's text from haruspex's. It is the IR's `description` on a field, a parameter and an object, from IR 0.33.

*Escaping.* A description is text from the database, kept with LF line endings. Half of a UTF-16 surrogate pair alone is no
text (UTF-8 can't hold it, and the IR's reader refuses it), so a value holding one is passed over with HAR1024; no source gives
one today, since a script is decoded with replacement and a package is XML. In a string literal it is escaped as JSON
escapes it. In a comment each of its lines is a line of the comment, blank ones and trailing space left out; `*/` is written
`*\/`, `U+2028` and `U+2029` as spaces, so nothing in it ends the comment early; and each `@` is written `\@`, since TypeScript
reads an `@` after a space as a tag (observed with TypeScript 6.0.3), and `@deprecated` would strike the field out in an editor.

*The IR.* `description` (0.33) on a field, a parameter and an object, left out where there is none. A document that records
one is labelled 0.33; one that records none keeps the label it had, as [decision 0122](0122-the-ir-records-the-packages-declared-builds.md)
did for 0.31. The reader refuses `description` before 0.33, a 0.33 document that records none, and an empty description.

*`check`.* A description is not a claim: it says nothing of what a value can be, so `IrComparison` reports no change in it,
as it reports none in a field's `from`, and a fit against a baseline ignores it. A changed description still fails `check`,
because the generated module's text changes, and the run names the file, as it does for a changed comment. So a change of
description is caught without being called a change of contract. A document that gains its first description moves from its
label (0.32 or earlier) to 0.33, and one that loses its last moves back; the reader reads both, so the move alone is neither a
change of claim nor a change of version (HAR6012), only the module's changed text (HAR6015).

**Context.** The design says throwing away something known is a defect, and maps a description held in `MS_Description` to
`.describe()`. Until now the package reader listed `SqlExtendedProperty` among what it passes over, and the script reader
passed over every `EXEC`. The vendored corpora hold descriptions in both forms: the WideWorldImporters project 407 calls with
named arguments, under the name `Description`; the AdventureWorks script 538 by position, under `MS_Description`.

**Alternatives.**
- *Give a field the description of each column in its `from`.* `from` lists every column a value is read from, through
  functions and arithmetic, so `UPPER(name)` and `a + b` would take descriptions of values they aren't.
- *A description as a claim, reported by `check` as a change.* A fit against a baseline would then fail, or have to set
  aside, a change no caller can observe. The changed module's text already fails `check`.
- *`.meta({ description })` in Zod.* It holds the same in the same registry; `.describe()` says the one thing written, and its
  type is the schema's own, so inference is unchanged.
- *Read only `MS_Description`.* The WideWorldImporters project writes `Description`, so the corpus would gain nothing.
- *The last of two descriptions wins.* A folder doesn't say which script runs last, and the server refuses the second add.

**Reason.** A description belongs to the thing described. Written onto exactly that thing, and onto nothing derived from it,
it can never describe a value it isn't, and a database's own documentation reaches the editor beside each field.
