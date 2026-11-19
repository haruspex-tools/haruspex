# The IR says where each field came from

**Date:** 19 Nov 2026

**Decision.** IR 0.25 records, for each field, the table columns its value is read from (`from`: each column as
`schema.table.column`, its type, and where it is declared), and, in a document combining two databases, the field as each
database gave it before they were combined (`sides`). With the null reasons the IR already holds, each a rule and where it
applied, that is the field's derivation: the column it starts from and each rule that acted on it on the way to the result.
`haruspex explain` reads only the committed IR, never the SQL. A document is labelled by what it says, as
[decision 0072](0072-a-document-written-with-a-baseline-is-the-combined-one-and-says-so.md) labels a combined one: 0.25 when a
field says where it came from, which a document inferred from SQL almost always does, else 0.24 when it combines two
databases, else 0.23. A reader of 0.25 reads all three, and refuses `from` or `sides` in a file labelled 0.23 or 0.24.

**Context.** A team that disagrees with a type needs the reason, and the people who disagree are usually the application's
developers, whose repository holds the IR and the generated code but often not the database's SQL. The inference already knew
each reason a value may be null, and where; it didn't say where the value started, so a reader couldn't tell a field that is
nullable because its column is from one that an outer join made nullable, or say what a type was widened from.

**Alternatives.**
- *Infer again when asked*, from the config file and the SQL. The IR stays smaller, but `explain` then works only where the SQL
  is, which is where it is needed least.
- *Record every rule the inference applies, each type rule included*, as a tree per field. Most rules leave a value as they
  found it, so the tree would be mostly noise, and the IR several times larger. The rules that change what a reader sees are the
  null reasons, already recorded, and a type that differs from its column's, which the column's type shows.
- *Carry the source columns as a fact of every value*, joined like the others, so that they pass through variables, temporary
  tables and calls. That is a witness in every value the fixed points compare, for a record nothing is typed from. Reading
  them from the select list, and through the derived tables, CTEs and views the query reads, covers the results people ask about.
- *Label every document 0.25.* Every document that says where its fields came from is labelled 0.25 anyway; one that says
  nothing of it means what it meant in 0.23 or 0.24, and is labelled so, as decision 0072 does.

**Reason.** The IR is the file the application has. A derivation that needs the SQL to be shown can't be shown to the people
who ask for it.

**What it costs.** Each field that reads a column carries one entry per column, its name, type and place: about five lines of
the file, against twenty or more for a field's type and facets. A combined document carries each side's presence and reasons
again. The IR is the file the application's repository keeps, so the lines are the price of a derivation it can show.

**In the code:** `IrField.From`, `IrSourceColumn`, `IrField.Sides`, `IrFieldSides`, `IrFieldSide`, `IrDocument.Version`,
`IrJsonReader`; `Typer.Reads.cs` in the SQL Server front end; `IrCombination`; `IrVersionTests`.
