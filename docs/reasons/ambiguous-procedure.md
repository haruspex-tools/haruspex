# The unknown reason `ambiguous-procedure`

An unknown reason, `IrUnknownReason.AmbiguousProcedure` in the IR's model. A value or a result is unknown because the procedure it calls is named without a schema that decides which.

## What it means

An unqualified procedure name that more than one schema has: which runs depends on the caller's default schema.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `An_unqualified_name_two_schemas_have_is_ambiguous` (`ExecTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
