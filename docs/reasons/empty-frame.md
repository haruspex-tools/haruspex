# The null rule `empty-frame`

A null rule, `IrNullRule.EmptyFrame` in the IR's model. A value may be null, or a result missing, because a window function over a frame that may hold no row is null there.

## What it means

It is a window function over a frame that may hold no row: an aggregate, `FIRST_VALUE` or `LAST_VALUE` whose `ROWS` frame may leave out the current row (`ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING` has nothing before the first row) is NULL there, over a column never null too. The default frame, and any frame that holds the current row, is never empty.


## What it rests on

Observed against SQL Server in `A_frame_that_may_leave_out_the_current_row_can_be_empty_and_give_null_over_a_never_null_column` (`FunctionObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
