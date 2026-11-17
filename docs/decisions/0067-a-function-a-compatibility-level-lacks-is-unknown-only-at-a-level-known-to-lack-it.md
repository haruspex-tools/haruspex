# A function a compatibility level lacks is unknown only at a level known to lack it

**Date:** 17 Nov 2026

**Decision.** A built-in function that only some compatibility levels have is typed as at a level that has it, unless the
project's level is known and lacks it: then its use is unknown, with a reason naming the level it needs. The first is
`STRING_SPLIT`, which needs 130: in a project whose level is below 130 it is unknown (`unsupported-source`), and in one at 130
or above, or whose level isn't known, it gives `value` (and `ordinal`), typed. The level is the project's `CompatibilityMode`, or
its DSP's, or a DACPAC's header (`DatabaseSettings.CompatibilityLevel`); a folder of scripts has none.

**Context.** Observed on SQL Server 2025 on 17 Nov 2026, in a database of the test's own whose level each step changes
(`String_split_needs_compatibility_level_130_and_has_its_ordinal_from_there`): below 130 SQL Server doesn't find
`STRING_SPLIT` (208), in a query and when a procedure that reads it runs, though the procedure is created; from 130 it is there,
and so is its `ordinal`. The scalar regular-expression functions, by contrast, are there at every level from 100, and
`REGEXP_LIKE` only at 170, where it is a predicate that types nothing
(`The_regular_expression_functions_but_regexp_like_are_there_at_every_compatibility_level_and_refuse_what_they_cannot_read`), so
neither needs the rule. A level can't be changed inside the transaction the other observation tests run in, so these use a
database of their own.

**Alternatives.**
- *Unknown wherever the level isn't known.* Sound, but it gives up the function wherever the source doesn't record a level, a
  folder of scripts always among them, to guard against a statement that fails rather than one that gives other columns.
- *Ignore the level.* At a level known to lack the function, the procedure fails when it reaches the statement, and a typed
  result would describe a statement the project's database never runs: a claim about nothing, which reads as a claim about
  something.

**Reason.** At a level that lacks the function, the statement is an error (208), never a result of other columns, so wherever a
result comes back it has the columns typed here: typing it at an unknown level claims nothing the server can break. Where the
project says its level lacks the function, the user is better told that than shown a result the database will never return.
The two corpora read no `STRING_SPLIT` under a level below 130, so their figures don't move.

**See also:** the Inference README, "`STRING_SPLIT`"; [decision 0047](0047-a-star-is-bound-against-the-tables-the-project-defines.md).
