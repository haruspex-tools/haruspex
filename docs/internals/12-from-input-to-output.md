# From input to output (the CLI)

The other chapters are about what haruspex works out. This one is about the run that carries a project from its files to
the two it writes: how `haruspex generate` is put together from steps, how each step says what went wrong without
stopping the program to say it, and why a run reports every problem it can rather than the first. The ideas are old and
have names: *result types* against exceptions, *railway-oriented programming* and its *applicative* side, and the
*error recovery* every compiler does. A running example, a small library's database, goes through all of it.

## A run is a pipeline of steps

`haruspex generate` (`Generate`) is eight steps, each a method short enough to read at once, each taking what the one
before it gave:

```mermaid
flowchart LR
    config["read the config file"] --> settle["settle the run"]
    settle --> project["read the project"]
    project --> select["select"]
    select --> infer["infer"]
    infer --> emit["emit"]
    emit --> write["write"]
    write --> noted["note the unknowns"]
```

- **Settle** works out what the run is asked: it reads the config file (named, or `haruspex.json` in the working folder
  or the nearest above it), then settles the folder to read, the folder to write and the `--include` patterns, the
  command line over the config file.
- **ReadProject** reads every `.sql` file into the model.
- **Select** picks the procedures to type.
- **Infer** infers their results and applies the config file: overrides, declared results, names.
- **Emit** writes the Zod module, in memory.
- **Write** puts both files in place.
- **Noted** lists each unknown in what was written.

Each step can fail because of its input: a config file that isn't JSON, a folder that isn't there, a pattern that
matches nothing, two generated names that clash. What matters is how a step says so.

## Results against exceptions

The obvious way, in C# and most languages since CLU and Ada, is an *exception*: a step that can't go on throws, and
whoever knows what to do catches. Exceptions are the right tool for what nobody can handle where it happens, such as a
disk that fails or a bug. For a user's mistake they are a poor fit, for three reasons:

- **The signature hides them.** Nothing in `HaruspexConfig Read(string path)` says it can fail, or how. Java tried to
  fix this with *checked* exceptions, which a method must declare; most later languages didn't follow, because they
  spread through every signature above them.
- **The first one wins.** A step that throws at its first problem can't report the second, so a user fixes one mistake,
  runs again, and meets the next.
- **The control flow is invisible.** To know what happens after a mistake, a reader looks for the `catch`, which may be
  several calls away.

The alternative is to make failure a value. A function returns either its result or what went wrong, and its type says
so: Haskell's *Either*, OCaml's and F#'s *result*, Rust's *Result* (whose `?` operator passes a failure up without a word
more). In haruspex it is `Outcome<T>` ([decision 0037](../decisions/0037-steps-return-outcomes.md)), a closed hierarchy of
two cases:

- `Succeeded<T>`: the value, and what was found on the way, warnings and notes, never an error.
- `Failed<T>`: what was found, at least one of it an error, and no value.

Each case holds its invariant from its constructor, so no outcome has a value beside an error, or neither. What was
found is a list of `Diagnostic`s, each a stable code, a message and a place
([decision 0036](../decisions/0036-diagnostics-have-stable-codes.md)); the codes have pages in
[docs/diagnostics](../diagnostics/README.md). Exceptions stay for faults, and the CLI turns any that reaches it into exit
code 3.

## The railway

Composing such functions by hand means testing each result before calling the next. Scott Wlaschin's
*railway-oriented programming* (2014) draws the pattern: each step is a switch with two tracks out, success and failure;
on the success track the value goes on to the next step, and once on the failure track a value never comes back. In
haruspex the switch is `Outcome.Then`: on a success it runs the next step on the value and puts what both found
together; on a failure it doesn't run the next step, and carries the failure, with everything found so far, to the end.
The functional literature calls it *monadic bind*. `Generate.Run` is the railway written down:

```csharp
var run = Settle(request)
    .Then(ReadProject)
    .Then(Select)
    .Then(Infer)
    .Then(Emit)
    .Then(Write)
    .Then(Noted);
return Report(run, output, error);
```

At the end, `Generate.Report` writes everything found, in the order it was found, and gives the exit code from what failed: the
input itself or the write, 1; the command line or the config file, 2.

The running example is a library with two procedures. One reads JSON a column holds, whose shape haruspex can't see;
the other runs SQL it builds as it runs.

```sql haruspex-example
CREATE TABLE lib.book (
    id int NOT NULL PRIMARY KEY,
    title nvarchar(100) NOT NULL,
    extra nvarchar(max) NULL);
GO
CREATE PROCEDURE lib.list_books AS
SELECT b.id, b.title, json_query(b.extra) AS extra FROM lib.book AS b FOR JSON PATH;
GO
CREATE PROCEDURE lib.find_books @where nvarchar(200) AS
DECLARE @sql nvarchar(max) = N'SELECT b.id FROM lib.book AS b WHERE ' + @where;
EXEC sp_executesql @sql;
```

Its first config file has two mistakes, one in each of two steps: a setting with a value it can't have, and a pattern
for procedures the project doesn't have.

```jsonc haruspex-config
{
  "source": "db",
  "out": "generated",
  "objects": "loose",
  "schemas": { "lib": { "include": ["list_*", "find_*", "lend_*"] } }
}
```

```text haruspex-run
haruspex.json:4:14: error HAR3005: objects: must be "strip" or "strict", not "loose".
```

Only the first is reported. Reading the config file failed, so no step after it ran: there is no selection to check a
pattern against until there is a config to select with. That is the failure track at work. Here it costs something: the
pattern `lend_*` is a mistake of its own, not an echo of the first, and the run can't see it yet, because the step that
can needs the config the first mistake took away. Sequencing trades that delay for never reporting a problem that only
follows from another (a later section comes back to it). Corrected, the next run reaches selection and stops there:

```jsonc haruspex-config
{
  "source": "db",
  "out": "generated",
  "objects": "strict",
  "schemas": { "lib": { "include": ["list_*", "find_*", "lend_*"] } }
}
```

```text haruspex-run
haruspex.json:5:57: error HAR4002: schemas.lib.include[2]: 'lend_*' matches no procedure in the schema "lib". Correct it, or remove it.
```

## What a successful run says

With the pattern corrected, the run succeeds:

```jsonc haruspex-config
{
  "source": "db",
  "out": "generated",
  "objects": "strict",
  "schemas": { "lib": { "include": ["list_*", "find_*"] } }
}
```

```text haruspex-run
haruspex.json: warning HAR4204: lib.find_books: its result sets are unknown: EXEC sp_executesql @sql runs dynamic SQL, a string built when the procedure runs, so its result sets are not known. (project.sql:11:1) Declare them in haruspex.json under procedures["lib.find_books"].results, as "none" or a JSON Schema, or declare the results of the procedure that runs it.
db/project.sql:7:23: note HAR2013: lib.list_books results[0][].extra: json_query(b.extra) reads JSON that is not built here, so its shape is unknown.
haruspex: 2 procedures, 2 unknown(s). Wrote generated/haruspex.ir.json and generated/haruspex.zod.ts.
```

A success can still say things. `lib.find_books` runs dynamic SQL, so its result sets are unknown and only a
declaration can give them: that is a warning, with what to declare. The `extra` key of `lib.list_books` is unknown, a note
with its reason and where it is. Every unknown is reported, but each once: the dynamic SQL is already the warning, so it
has no note of its own. Neither changes the exit code, which is 0.

## Every problem, not the first

A compiler that stopped at its first error would be tiresome, so every compiler since the earliest tries to report all of
them. The textbooks call it *error recovery* (Aho, Lam, Sethi and Ullman, *Compilers*, chapter 4): after an error, the
parser skips to a point it can resume from and carries on. Its danger is the *cascade*, errors that are only echoes of the
first, a missing declaration reported again at every use. Good recovery reports each real error once, and says nothing it
can't stand behind.

A pipeline gets the same from two ways of composing. Steps that need each other's values go in sequence, with `Outcome.Then`,
and a failure ends the line: that is what prevents the cascade, since a step that needs a value it hasn't got is never run.
Steps that don't need each other's values go side by side, and both run whatever the other does. That is
`Outcome.Combine`: both values when both succeed, and otherwise a failure with what each found. Monads can't do this on
their own, since bind needs the first value before it can run the second; it is the *applicative* style (McBride and
Paterson, *Applicative programming with effects*, 2008), which libraries for validation (*Validated* in Scala's Cats,
*Validation* in Haskell) are built on.

In `Generate.Settle`, the `--include` patterns don't depend on the config file, so a config file that doesn't read and a
malformed pattern are both reported. The two folders depend on the config file, and whether anything is selected on the
config file and the patterns, so they follow the config file with `Outcome.Then`, combined with each other. A run with no folder, no output and a malformed pattern hears about
all three, and nothing about a selection never made. With no config file:

```sql haruspex-example
CREATE TABLE lib.book (id int NOT NULL PRIMARY KEY);
```

```text haruspex-run --include a.b.c
haruspex: error HAR6003: give the folder of .sql files to read, or a config file with "source".
haruspex: error HAR6005: give --out, the folder to write to, or a config file with "out".
haruspex: error HAR6006: invalid --include pattern: 'a.b.c' is not a two-part name such as sales.get_order, sales.* or *.list_*.
```

## Writing without half an output

The last step writes two files, and a run must never leave one new beside one old: an application compiled against a new
module and an old IR, or the reverse, would be checked against a contract that isn't the one in force. No file system
replaces two files as one, so `Generate.Write` comes as close as a rename allows. Both files are written in full to
temporary names in the output folder itself, so that putting each in place is a rename within one file system: POSIX
promises such a rename is atomic, so a reader sees the old file or the new one, never part of either; Windows makes no
such promise for the replacing call .NET uses there, so on Windows the guarantee is only that each file is written in full
before it is put in place. Then the IR is put in place, keeping the old one aside, and the module after it; if the module
can't be, the IR is put back as it was. Two cases can still leave them out of step, and the CLI's README names both: a
process killed between the two renames, and a file system that refuses to put the IR back, when the error says so and
keeps the old IR's backup. A run that fails earlier writes nothing, and doesn't even create the folder, since the folder
is made in `Generate.Write`, after every other step has succeeded.

## Where to read more

- Scott Wlaschin, *Railway oriented programming* (2014), the talk and the article, for the pattern and its pictures.
- Conor McBride and Ross Paterson, *Applicative programming with effects* (Journal of Functional Programming, 2008), for
  why independent steps compose differently from dependent ones.
- Alfred Aho, Monica Lam, Ravi Sethi and Jeffrey Ullman, *Compilers: Principles, Techniques, and Tools*, section 4.1.4, on
  error recovery.
- OASIS, *Static Analysis Results Interchange Format (SARIF) 2.1.0*, for the levels `error`, `warning` and `note`, and rule
  identifiers such as haruspex's codes.
