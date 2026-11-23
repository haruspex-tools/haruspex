# A table-valued parameter is an array of strict rows of its type

**Date:** 19 Nov 2026

**Decision.** A `READONLY` parameter of a table type the project defines is an array of the type's rows. A row has every column of
the type, in the type's order, each a required key whose value is checked as a scalar parameter of the column's type is (its
length, range and digits, and the facts the type's CHECK constraints prove), and which takes NULL only where the column does. The
row is strict, as the parameters are: a key it doesn't list is refused. The parameter may be left out, which the procedure reads as
an empty table, or sent as an empty array, but it can't be NULL. A type with an identity, computed or `rowversion` column, or a
column of a type haruspex doesn't model, is no array: its parameter is unknown, saying why, and can only be left out. The
generated function sends the rows as the mssql driver's `sql.Table`, named by the type's schema and name apart, with every column
declared in the type's order as a scalar parameter of its type is sent (a date or time as text). Inside the body, the parameter
and a variable declared with a table type are table variables of the type's columns, keys and CHECK constraints.

**Context.** Table-valued parameters are common in older corporate code (the harvest of the public corpora found 143), and a procedure that took one
was unknown in its parameter and in every column a query read from it. What the server does with a row was observed through the
mssql driver, case by case (`ParameterObservationTests`):

- the columns are matched by place, not name: sent in another order, the values go to other columns without an error;
- every column must be sent (500), even one with a default, and a default never applies to a row sent: NULL in a `NOT NULL`
  column with one is refused (515), and NULL in a nullable one is NULL;
- a type with an identity, computed or `rowversion` column refuses every table sent, even one of no rows (1077, 271, 273), and
  the call runs only when the parameter is left out;
- a key refuses a row that shares it (2627), unless it is declared `IGNORE_DUP_KEY = ON`, which drops the row without an error;
  a CHECK refuses a row that breaks it (547); a string longer than its declared column is refused (8037);
- NULL for the parameter is refused (206), and one left out is an empty table;
- the driver's error for a refused row has no number and no message of its own: the server's (2627, then 8061, "the data for
  table-valued parameter doesn't conform") come before it, in `precedingErrors`.

**Alternatives.**
- *Leave a column with a default out of the row, or make it optional.* The server never applies the default to a row sent, and
  the driver can't send a table without the column, so the key would be one the function must fill anyway, with NULL, which a
  `NOT NULL` column refuses.
- *Make a nullable column's key optional, sent as NULL when left out.* Shorter to write, but a caller may read a key left out as
  "the default", which it isn't; every key required says what is sent.
- *Leave the identity and computed columns out of the row.* The server refuses the table whatever is sent, so the function would
  fail on every call; an unknown that can only be left out says what the type allows.
- *Check keys in the schema* (no two rows sharing one). The server refuses it, as the driver's own error; a schema check would be
  exact only under the column's collation, which a client can't always reproduce, and a key that ignores duplicates refuses
  nothing.
- *Name the type in the driver's way, `[sales].[line_list]`, parsed by the driver.* Its parser can't hold a `]` in a name;
  setting the table's schema and name apart sends them as they are.

**Reason.** The rule is the parameters' rule (decision 0054, decision 0009) applied to each row: everything the schema accepts is
a row the server takes, and what the schema can't check (a key shared) is the server's own error. `MssqlFunctionTests` and
`ParameterObservationTests` hold both to the server.

**In the code:** `TableTypes` and `TableRowShape` (Inference), `TableType` (Model), `BodyDeclarations` (Semantics),
`ParameterLowering.TableRow` (Zod), `TableValue` (mssql); `ParameterTests.TableTypes.cs`, `MssqlFunctionTests.TableTypes.cs`,
`ParameterObservationTests.TableTypes.cs`.
