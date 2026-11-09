# Fixture: values

An invented table of samples, one column per scalar type and precision, with tags as children. It exists for
the differential tests: their seed (`tests/Haruspex.DifferentialTests/Seeds/values.sql`) fills it with boundary
values, strings of their full length, keys at `int`'s edges, zero and non-zero fractions, zero and non-zero offsets, padded strings and a row of nulls, and each
procedure's real output is checked against the IR inferred for it.

| Procedure | Covers |
|---|---|
| `vals.list_samples` | every scalar type in `FOR JSON PATH`; a null's key left out; no rows |
| `vals.list_samples_with_nulls` | the same with `INCLUDE_NULL_VALUES` |
| `vals.get_sample` | `WITHOUT_ARRAY_WRAPPER`, and no row at all when nothing matches |
| `vals.sample_rows` | the same columns as a rowset, as SqlClient types them |
| `vals.list_nested` | nested objects from dotted aliases, two deep, an empty subquery |
| `vals.list_nested_with_nulls` | the same with `INCLUDE_NULL_VALUES` |
