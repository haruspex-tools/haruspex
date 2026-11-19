# The IR carries a table type's definition

**Date:** 19 Nov 2026

**Decision.** An IR document holds, in `tableTypes`, the definition of each table type its parameters take: the type's schema and
name, its columns in order (each with its SQL type, its type and facts as a scalar, whether it is nullable, how the server fills
it when a row can't, and its collation when known), its keys (whether each ignores duplicates) and its CHECK constraints'
conditions, each on one line. A table-valued parameter names its type there (`tableType`), and its own type is an array of a named row
type owned by the table type (`identity: { "tableType": … }`), shared by every parameter of the type. This is IR 0.26. A document
is labelled with the oldest version that says all it says: 0.26 when it defines a table type, else 0.25 when a field says where its
value came from (decision 0078), else 0.24 when it combines two databases, else 0.23, so every run that meets no table type writes
what it wrote before. A reader of 0.26 reads all four, and
refuses a label that disagrees with what the document says. `check --baseline` judges a table-valued parameter by the two
definitions (decision 0068), and `generate --baseline` narrows a table type's columns as it narrows any parameter (decision 0073).

**Context.** Without the definition, a table-valued parameter could only be an unknown, and `check --baseline` refused it on every
run, even unchanged, since a type may change under its name (V1a's follow-up). The server matches a row's columns by place, so
what fits is decided by the columns' order and types, the keys and the conditions, all of which only the definition says.

**Alternatives.**
- *Put the definition in the row type alone.* The row is what a client sends, and a type with an identity column has no row a
  client can send, yet its definition still decides whether an unchanged parameter fits. A key or a condition the IR has no facet
  for (a CHECK over two columns) is no fact of a field either.
- *A table type as a new type node.* Every emitter would need a case for it, where an array of a named object type is a node each
  already writes, and the CM5 split already places by references.
- *Label every document 0.26.* Every module's header and every committed IR would change in a run that meets no table type, and a
  0.24 reader would refuse files it reads correctly; decision 0072 labels by content for the same reason.
- *Compare a CHECK by its facts only.* A condition the IR can't express as a fact would be lost, and a change to it would pass. The
  condition's text is kept on one line, its comments and runs of white space each one space, so two sources (a folder and its
  DACPAC, which keeps the script as written) write it alike; a text that differs otherwise, even only in case or spacing, is
  refused, which claims less. ScriptDom's generator would rewrite it more fully, but isn't safe to compile ahead of time.

**Reason.** The IR is the contract other programs read, and a parameter that names a type it doesn't define is one no program can
check or compare. Carrying the definition is what lets an unchanged parameter fit, and labelling by content keeps every other
document as it was.

**In the code:** `IrTableType`, `IrTableColumn`, `IrTableKey`, `IrColumnGeneration`, `IrParameter.TableType`,
`IrTypeOwner.TableType`, `IrDocument.TableTypes` and `IrDocument.Version`, `IrJsonReader`, `IrFit.TableTypes.cs`,
`IrCombination.TableTypes.cs`; `IrFitTests.TableTypes.cs`, `IrCombinationTests.TableTypes.cs`, `BaselineFitTests.TableTypes.cs`,
`CombinedTypesTests.TableTypes.cs`, `IrJsonReaderTests.TableTypes.cs`, `IrVersionTests`.
