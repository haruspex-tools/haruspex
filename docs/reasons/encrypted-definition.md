# The unknown reason `encrypted-definition`

An unknown reason, `IrUnknownReason.EncryptedDefinition` in the IR's model. A value or a result is unknown because the object is encrypted and its text is not in the source.

## What it means

A module created `WITH ENCRYPTION` whose definition the source doesn't hold: a DACPAC extracted from a database keeps a placeholder in its place, since the server gives no one the text. What it returns can only be declared.

## What to do

To change it, declare the results in the config file's `"results"`.

## What it rests on

There is no engine rule to observe here: it says what haruspex does. `An_encrypted_procedure_whose_text_the_source_lacks_has_unknown_results_with_their_own_reason` (`ExecTests`) shows haruspex giving it.

See [why a type is what it is](README.md) for how `haruspex explain` and the generated comments use this code.
