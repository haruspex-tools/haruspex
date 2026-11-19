# The unknown reason `dynamic-sql`

An unknown reason, `IrUnknownReason.DynamicSql` in the IR's model. A value or a result is unknown because it runs dynamic SQL, a string built when it runs.

## What it means

Dynamic SQL (`EXEC (@sql)`, `sp_executesql`): what it runs is a string built when the procedure runs.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `Dynamic_sql_makes_the_results_unknown` (`ExecTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
