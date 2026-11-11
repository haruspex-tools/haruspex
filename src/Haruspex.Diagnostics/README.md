# Haruspex.Diagnostics

Two small types shared by haruspex's layers: `SourceLocation` (a file relative to the source root, with `/`
separators on every platform, and a 1-based line and column) and `Diagnostic` (a message, where it applies, and its
severity). Both print in the `file:line:column: error: message` form that editors and CI annotate, with `warning`
for a warning.

A diagnostic is an **error**, which stops analysis (SQL that doesn't parse, say), or a **warning** about a gap the
results can't show (a table the project doesn't define), after which the results stand (`DiagnosticSeverity`).

It is a package of its own only because the reusable layers,
[Haruspex.SqlServer.Model](https://github.com/sharpblue/haruspex/blob/main/src/Haruspex.SqlServer.Model/README.md) and
[Haruspex.SqlServer.Sources](https://github.com/sharpblue/haruspex/blob/main/src/Haruspex.SqlServer.Sources/README.md), have these types in their public API. It
deliberately knows nothing else: no SQL, no files, and no diagnostic codes yet, since nothing needs them.

It is part of [haruspex](https://github.com/sharpblue/haruspex), and pre-release: its API will change.
