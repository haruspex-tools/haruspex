# haruspex

A tool that carries a SQL Server database's types into TypeScript by static analysis. It reads a database project's source
(a folder of `.sql` files, an SSDT `.sqlproj` or a DACPAC) and infers what each stored procedure takes and returns. From that
it writes [Zod](https://zod.dev) schemas and types, JSON Schema, and a function for each procedure over the
[mssql](https://www.npmjs.com/package/mssql) driver. It never connects to a database.

This package is the `haruspex` command. It holds no program of its own: npm installs the native binary for your platform from
one of its optional dependencies (`@haruspex.tools/linux-x64`, `@haruspex.tools/linux-arm64`,
`@haruspex.tools/darwin-arm64`, `@haruspex.tools/darwin-x64` or `@haruspex.tools/win32-x64`), and the command runs it. No .NET
runtime is needed. Linux needs glibc.

```sh
npx haruspex generate     # settings from haruspex.json, found by walking up
npx haruspex check        # in CI: fail if the module in the output folder would change
```

The JSON Schema of `haruspex.json` is in this package, so an editor can check the file with no network:

```jsonc
{ "$schema": "./node_modules/haruspex/haruspex.schema.json" }
```

It is pre-release: its commands, options and output will change. The documentation is in
[the repository](https://github.com/haruspex-tools/haruspex#readme).
