# The null rule `missing-for-action`

A null rule, `IrNullRule.MissingForAction` in the IR's model. A value may be null, or a result missing, because a `MERGE`'s `OUTPUT` has no value for it for some of its actions.

## What it means

It is a column a statement that takes several actions reports, which has no value for some of them: in SQL Server's `MERGE` ... `OUTPUT`, a `deleted` column for the rows it inserts, an `inserted` column for the rows it deletes, and a column of its source for the rows it changes because they are not matched by the source.


## What it rests on

Observed against SQL Server in `Merge_output_has_null_deleted_columns_for_inserted_rows_null_inserted_columns_for_deleted_rows_and_null_source_columns_when_not_matched_by_source` (`DmlObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
