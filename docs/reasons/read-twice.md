# The null rule `read-twice`

A null rule, `IrNullRule.ReadTwice` in the IR's model. A value may be null, or a result missing, because `COALESCE` reads its argument a second time, which may then be null.

## What it means

It is a `COALESCE` one of whose arguments, a subquery or a call, is tested and then read again: the second read may be NULL (another statement changed the rows between them, or the call gives another value). Before 0.12 the argument's own reasons were given instead. No seed reaches it; it is observed.


## What it rests on

Observed against SQL Server in `Coalesce_reads_a_subquery_twice_so_it_can_be_null_where_isnull_cannot` (`ExpressionObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
