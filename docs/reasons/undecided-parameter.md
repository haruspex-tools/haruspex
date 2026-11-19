# The unknown reason `undecided-parameter`

An unknown reason, `IrUnknownReason.UndecidedParameter` in the IR's model. A value or a result is unknown because nothing in the query decides the parameter's type.

## What it means

A query file's parameter whose type its uses don't decide: nothing in the file says what it is, or two uses say different things. A declaration in the file decides it instead.

## What to do

To change it, declare the parameter's type in the query file (`-- @param`).

## What it rests on

Observed against SQL Server in `Where_the_server_can_not_decide_haruspex_leaves_the_parameter_unknown` (`QueryParameterObservationTests`).

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
