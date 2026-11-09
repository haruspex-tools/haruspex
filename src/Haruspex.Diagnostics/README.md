# Haruspex.Diagnostics

Two small types shared by haruspex's layers: `SourceLocation` (a file relative to the source root, with `/`
separators on every platform, and a 1-based line and column) and `Diagnostic` (a message, and where it applies).
Both print in the `file:line:column: error: message` form that editors and CI annotate.

It is a package of its own only because the reusable layers,
[Haruspex.SqlServer.Model](https://github.com/sharpblue/haruspex/blob/main/src/Haruspex.SqlServer.Model/README.md) and
[Haruspex.SqlServer.Sources](https://github.com/sharpblue/haruspex/blob/main/src/Haruspex.SqlServer.Sources/README.md), have these types in their public API. It
deliberately knows nothing else: no SQL, no files, no severity levels or codes yet (every diagnostic today is an
error that stops analysis).

It is part of [haruspex](https://github.com/sharpblue/haruspex), and pre-release: its API will change.
