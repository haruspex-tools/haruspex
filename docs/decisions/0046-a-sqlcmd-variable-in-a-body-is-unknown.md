# A SQLCMD variable in a body is unknown

**Date:** 15 Nov 2026

**Decision.** A string literal in a body or a CHECK constraint that holds a SQLCMD variable, `'$(Region)'`, is unknown, with the
reason `sqlcmd-variable` (IR 0.21), from every kind of source: a folder of scripts, a DACPAC and a project alike. So is everything
drawn from its text: its length, its value, a value set built from it (a CASE, a UNION, a CHECK), a JSON document it spells, a
conversion judged by it, a key it would prove unique. A key holding one, an alias `[$(Label)]` or a `json_object` key, leaves
the keys of what it names unknown. A variable that a script sets itself with `:setvar` is replaced before the script is read,
as before, so it isn't one of these. A reference is `$(name)` where the name has no space, quotation mark, parenthesis or dollar
sign, as sqlcmd reads one (`SqlcmdVariableReference`); a name bound by it, `[$(LedgerDb)].dbo.t`, is still bound by its name
([decision 0045](0045-a-project-s-references-are-read-from-where-its-build-takes-them.md)).

**Context.** A deployment replaces each variable in the text before SQL Server sees it, and haruspex read the text as written: it
typed `select '$(Region)' as region` as `varchar(9)`, at most nine characters. Observed on 15 Nov 2026, with probe projects and the
test server:
- **A DACPAC** keeps the text as written in its bodies and constraints, comments included, and lists the project's declared
  variables in its header, each with an empty value, whatever the project gives. A variable it doesn't declare builds with no
  warning.
- **SqlPackage 170.5.96 publishing it** replaces a declared variable's value in literals, CHECK constraints and bracketed names,
  not in comments: `sys.sql_modules` holds `SELECT 'north-west-europe' AS region`, and `[$(Label)]` becomes the column `zone`.
  An empty value, given in a publish profile, gives `''`, and the CHECK `([region]='south' OR [region]='')`. A variable the
  package doesn't declare fails the publish, unless the publish is given it, when it is replaced too. A value holding a
  quotation mark is put in as text and ends the string: `'$(Region)'` with `1'+1+'` became the int `2`.
- **A project** builds the same package. Its declared variables' values aren't used by the build (decision 0045), and its build
  refuses a variable in an object's name (SQL70604), so a column can't be named by one; an alias can.
- **sqlcmd running a folder's script** replaces a variable from `-v` or from the environment; with neither, it reports the
  variable and sends the text unchanged. The script's own `:setvar` wins over both.

So only a script's own `:setvar` fixes a value. Anywhere else the value is the deployment's, and any value is possible: empty,
longer than the column, or not a string at all. `SqlcmdVariableObservationTests` deploys such text with values of each kind put
in, as the tools were seen to put them, and checks that every claim haruspex makes of the written text holds for each; on the
earlier rule seven of its fifteen cases fail.

**Alternatives.**
- *A string of unknown length.* It keeps the type, but a value with a quotation mark changes it, as observed; the claim would
  hold only for values a deployment isn't bound to give.
- *Read the values from the source.* A DACPAC records every value empty, and a project's values aren't what the build uses or
  what a publish is given; neither says what a deployment will set.
- *Unknown only where the source declares the variable.* A DACPAC that doesn't declare one can still be published with it, and a
  folder's script can be given any variable by `-v` or the environment, so nothing short of a `:setvar` in the script rules a
  replacement out. Text that only looks like a variable, `'$(5)'` in a message, becomes unknown too: rare, and the price of never
  claiming a deployment's value.
- *Leave the result unknown wherever a value could end its string.* A value that also ends the statement can rewrite it into
  anything, which no reading of the text can follow; haruspex assumes, as it does of every input, that the deployed statement is
  the one written, with each variable's value in its place as one expression.

**Reason.** The text a deployment runs is not the text written, and only the deployment knows the difference; claiming the
written text's facts would type a procedure no server runs. Nothing else in a body changes, so what the variable doesn't touch is
still claimed. The two corpora have no such literal, so their figures don't move.

**See also:** [HAR2023](../diagnostics/HAR2023.md); [The inputs and the model](../internals/02-the-inputs-and-the-model.md),
"SQLCMD variables"; [decision 0024](0024-an-ir-reader-reads-only-versions-whose-documents-mean-what-its-own-do.md).
