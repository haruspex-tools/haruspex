# Style fixture: each line below breaks one rule or shows one that passes

This file is wrong on purpose. `tests/prose/check.sh` runs Vale on it and expects exactly the alerts in
`style.expected`; the lines that should pass are here to show the rules don't overreach.

## A Heading In Title Case

## A heading about CROSS APPLY and haruspex, which passes

Hype: a powerful tool.

Minimiser: you simply run it.

Terms: turn the knob.

Terms: it returns raw JSON.

Punctuation: one thing — and another.

Punctuation: from 1 – 9.

Punctuation: see, e.g., the IR.

Punctuation: that is, i.e. this.

Punctuation: it works!

Name: Haruspex infers the shape.

Spelling: we tokenize the input, which no list names: the rule is a pattern.

Spelling: the center of the gray area.

haruspex begins this sentence.

Passes: `Haruspex.Ir` and Haruspex.Ir are namespaces, HaruspexCli is a type, and `x!` and `just` are code.

Passes: a read-only, well-known hyphen; and a sentence that ends with haruspex.

Passes: the size of a prize, JsonSerializer and ProcedureAnalyzer (identifiers), and `normalize` in code.
