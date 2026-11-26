# JSON stored in a column is typed only by declaration

**Date:** 11 Nov 2026

**Decision.** JSON built in the project is traced and typed exactly, wherever it goes inside the project. JSON stored in a
table's column is not: its content is unknown unless a declaration says what it is. A later version may *suggest* a
declaration from the writers it sees, but never assume one. Declarations of a column's JSON are not built yet.

**Context.** A procedure often reads JSON that another procedure wrote to a column, and haruspex can see that writer.

**Alternatives.**
- *Type the column from the writers in the project.* Another writer may exist: an application, a script run by hand, an
  import, or a writer from before the current code. What the project writes today says nothing about the rows already
  there.

**Reason.** A claim about built JSON is a claim about the engine, since both ends are in the source. A claim about stored
JSON would be a claim about every writer there ever was, which the source can't show. A declaration states it once, as a
claim a person makes, checked at runtime like any other.

**Amended** (21 Nov 2026, [decision 0093](0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md)). The
declarations are built: the config file's `columns` declares a column's JSON by a JSON Schema, and the declaration is part
of the column's type, read wherever the column is. The decision stands; its last sentence, that declarations were not built
yet, held until then.

**In the code:** the Inference README, "JSON built here, traced"; the IR README, "Live JSON versus JSON text".
