# A type is spelt with its defaults written out

**Date:** 14 Nov 2026

**Decision.** Wherever haruspex writes a declared type (an IR scalar's `sql`, the type a fact is attributed to, a message, and so
the generated module's comments), it writes the type with its defaults: a column declared `decimal` is `decimal(18,0)`,
`varchar` is `varchar(1)`, `datetime2` is `datetime2(7)`, `decimal(10)` is `decimal(10,0)`, as a computed value of each is
already written (`ResolvedType.Spelt`, `ScalarType.Text`). The model still keeps a type as it was written.

**Context.** A DACPAC holds the type DacFx resolved, with every argument, and not how the script wrote it. So the same database,
read from its scripts and from the DACPAC its project builds, spelt a bare type two ways: one IR said `decimal`, the other
`decimal(18,0)`, and a diff of the two would report a change where nothing changed.

**Alternatives.**
- *Spell a DACPAC's types without their defaults.* A script that writes `char(1)` or `datetime2(7)` would then differ the
  other way, and both are common.
- *Keep two spellings, and let a comparison know they are one.* Every consumer of the IR (a diff, a reviewer, the proof that a
  DACPAC reads as its folder does) would need the type catalogue to compare two files.

**Reason.** A type means its arguments, defaults included; writing them out is the one spelling both inputs can give, and the
one inference already wrote for every value it computed. The cost is a folder's output naming `decimal(18,0)` where the script
says `decimal`, which is what the column is.

**See also:** [The inputs and the model](../internals/02-the-inputs-and-the-model.md), "A DACPAC becomes the same model".
