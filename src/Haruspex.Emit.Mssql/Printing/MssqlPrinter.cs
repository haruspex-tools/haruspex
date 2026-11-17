using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Haruspex.Emit.Mssql.Planning;
using Haruspex.Ir;

namespace Haruspex.Emit.Mssql.Printing;

/// <summary>
/// Writes the planned functions as a TypeScript module: the only stage that knows TypeScript's syntax and the driver's API. It
/// decides nothing about what a function sends or reads.
/// </summary>
/// <remarks>
/// The module is self-contained, as every module haruspex writes is: its error classes and helpers are written into it rather
/// than imported from a package, so that a module builds, versions and leaves with its application alone, and several modules
/// in one application need nothing shared. Code that handles their errors tells them apart by name and kind, which are the same
/// in every module (<c>docs/decisions/0056-a-function-throws-three-kinds-of-failure.md</c>). It uses only TypeScript a type
/// stripper can erase (no parameter properties, no enums), so Node runs it as it is and <c>erasableSyntaxOnly</c> accepts it.
/// The layout is fixed, two spaces of indent and LF line endings, so the output is deterministic.
/// </remarks>
internal static class MssqlPrinter
{
    /// <summary>What every module defines: the interface a function takes, the two errors it throws, and where an issue is.</summary>
    private const string Prelude = """

        /** Anything that makes an mssql request: a ConnectionPool, a Transaction, or a test double. */
        export interface Queryable {
          request: () => sql.Request
        }

        /** One problem a schema found, as Standard Schema reports it: a message, and where in the value. */
        export interface Issue {
          readonly message: string
          readonly path?: readonly (PropertyKey | { readonly key: PropertyKey })[] | undefined
        }

        /**
         * A procedure's parameters were wrong, so nothing was sent. Code that doesn't import this module tells it by its name,
         * HaruspexParameterError, and its kind.
         */
        export class ParameterError extends Error {
          override readonly name = 'HaruspexParameterError'
          readonly kind = 'parameters'
          readonly procedure: string
          readonly issues: readonly Issue[]

          constructor(procedure: string, issues: readonly Issue[]) {
            super(`${procedure}: ${issues.map((issue) => (where(issue) ? `@${where(issue)}: ` : '') + issue.message).join('; ')}`)
            this.procedure = procedure
            this.issues = issues
          }
        }

        /**
         * What a procedure returned broke the contract its schema states: the database and this module are deployed out of step,
         * which no retry fixes. Code that doesn't import this module tells it by its name, HaruspexResultError, and its kind.
         */
        export class ResultError extends Error {
          override readonly name = 'HaruspexResultError'
          readonly kind = 'result'
          readonly procedure: string
          readonly part: string
          readonly issues: readonly Issue[]

          constructor(procedure: string, part: string, issues: readonly Issue[]) {
            super(`${procedure}, ${part}: ${issues.map((issue) => (where(issue) ? `${where(issue)}: ` : '') + issue.message).join('; ')}`)
            this.procedure = procedure
            this.part = part
            this.issues = issues
          }
        }

        /** Where in a value an issue is, as dotted keys. */
        function where(issue: Issue): string {
          return (issue.path ?? []).map((segment) => String(typeof segment === 'object' ? segment.key : segment)).join('.')
        }

        """;

    /// <summary>How a value is checked through Standard Schema: written when some function checks one.</summary>
    private const string Checking = """

        /** A schema as Standard Schema describes it, which Zod, Valibot and ArkType all implement. */
        interface Checks<T> {
          readonly '~standard': {
            readonly validate: (
              value: unknown,
            ) =>
              | { readonly value: T; readonly issues?: undefined }
              | { readonly issues: readonly Issue[] }
              | Promise<{ readonly value: T; readonly issues?: undefined } | { readonly issues: readonly Issue[] }>
          }
        }

        /** The value, checked by the schema; or the failure made from its issues, thrown. */
        async function checked<T>(schema: Checks<T>, value: unknown, fail: (issues: readonly Issue[]) => Error): Promise<T> {
          const result = await schema['~standard'].validate(value)
          if (result.issues) throw fail(result.issues)
          return result.value
        }

        """;

    /// <summary>How a FOR JSON result is read: written when some function reads one.</summary>
    private const string JsonReading = """

        /** The name SQL Server gives the one column of a top-level FOR JSON result. */
        const forJson = 'JSON_F52E2B61-18A1-11d1-B105-00805F49916B'

        /**
         * A FOR JSON result as one document, whether the pool parses JSON (parseJSON) or not: the driver joins its rows into one,
         * which is the document, or its text under the column's name. No row, or empty text, is null.
         */
        function readJson(set: unknown): unknown {
          const row: unknown = Array.isArray(set) ? set[0] : undefined
          if (row === undefined || row === null) return null
          if (typeof row === 'object' && !Array.isArray(row) && Object.keys(row).length === 1 && forJson in row) {
            const text: unknown = (row as Record<string, unknown>)[forJson]
            if (typeof text === 'string') return text === '' ? null : JSON.parse(text)
          }
          return row
        }

        """;

    /// <summary>How a rowset result is read: written when some function reads one.</summary>
    private const string RowsReading = """

        /** A rowset result: its rows, or none when its statement did not run. */
        function readRows(set: unknown): unknown {
          return set ?? []
        }

        """;

    /// <summary>The module as text.</summary>
    public static string Print(IReadOnlyList<MssqlFunction> functions, MssqlOptions options)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(options);
        var output = new StringBuilder()
            .Append("// Generated by haruspex from IR ").Append(IrDocument.CurrentVersion).Append(". Do not edit.\n")
            .Append("// The database module's functions: one for each procedure and query file, which runs it through the mssql driver,\n")
            .Append("// checking what goes in and what comes back. They belong on the server, which holds the connection and its credentials.\n");
        if (options.ServerOnly)
            output.Append("import 'server-only'\n");
        output.Append("import sql from 'mssql'\n");
        Imports(output, functions, options.SchemaModule);

        // Each helper only where a function uses it: a linter reports one that nothing calls.
        output.Append(Prelude);
        if (functions.Any(f => f.Bindings.Count > 0 || f.Results is not [] || f.HasOutput))
            output.Append(Checking);
        if (functions.Any(f => f.Results?.Contains(IrEncoding.Json) == true))
            output.Append(JsonReading);
        if (functions.Any(f => f.Results?.Contains(IrEncoding.Rowset) == true))
            output.Append(RowsReading);
        foreach (var function in functions)
            Function(output, function);
        return output.ToString();
    }

    /// <summary>
    /// The schemas the functions use, by name and sorted, and the types through one namespace, <c>types</c>, both from the schema
    /// module. A type is named for its procedure, and so, under camel-case naming, is the procedure's function, which a named
    /// import of the type would clash with.
    /// </summary>
    private static void Imports(StringBuilder output, IReadOnlyList<MssqlFunction> functions, string module)
    {
        var values = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var function in functions)
        {
            var name = function.Procedure.Name;
            if (function.Results is not [])
                values.Add(SchemaModuleNames.ResultsSchema(name));
            if (function.Bindings.Count > 0)
                values.Add(SchemaModuleNames.ParametersSchema(name));
            if (function.HasOutput)
                values.Add(SchemaModuleNames.OutputSchema(name));
        }

        if (values.Count == 0)
            return;
        output.Append("import type * as types from ").Append(Quoted(module)).Append('\n')
            .Append("import {\n");
        foreach (var value in values)
            output.Append("  ").Append(value).Append(",\n");
        output.Append("} from ").Append(Quoted(module)).Append('\n');
    }

    /// <summary>
    /// One function: check the parameters before the connection is touched, bind each, execute, and check what comes back. The
    /// driver's own error, a <c>RequestError</c>, passes through as it is. When every parameter may be left out, so may the
    /// parameters' object.
    /// </summary>
    private static void Function(StringBuilder output, MssqlFunction function)
    {
        var procedure = function.Procedure;
        var id = Quoted(procedure.Id);
        var name = procedure.Name;
        var fields = Fields(function).ToList();

        output.Append('\n').Append(function.Batch is null ? "/** Calls " : "/** Runs the query file ").Append(Comment(procedure.Id)).Append(". */\n")
            .Append("export async function ").Append(function.Name).Append("(db: Queryable")
            .Append(function.Bindings.Count > 0 ? ", params: types." + SchemaModuleNames.Parameters(name) + (function.Bindings.All(b => b.OnlyWhenGiven) ? " = {}" : "") : "")
            .Append("): Promise<").Append(
                function.ReturnsNothing ? "void"
                : function.ReturnsResultAlone ? fields[0].Type
                : "{ " + string.Join("; ", fields.Select(f => f.Name + ": " + f.Type)) + " }").Append("> {\n");

        if (function.Bindings.Count > 0)
        {
            output.Append("  const p = await checked(").Append(SchemaModuleNames.ParametersSchema(name))
                .Append(", params, (issues) => new ParameterError(").Append(id).Append(", issues))\n");
        }

        output.Append("  const request = db.request()\n");

        // A query file runs through sp_executesql: its text, and its parameters' declarations, are sp_executesql's own first
        // two parameters, and each of the query's is passed beside them by name, converted by the server to its declared type.
        if (function.Batch is var (text, declaration))
        {
            output.Append("  request.input('stmt', sql.NVarChar(sql.MAX), ").Append(Quoted(text)).Append(")\n");
            if (declaration.Length > 0)
                output.Append("  request.input('params', sql.NVarChar(sql.MAX), ").Append(Quoted(declaration)).Append(")\n");
        }

        foreach (var binding in function.Bindings)
            output.Append("  ").Append(Bind(binding)).Append('\n');
        output.Append(function.ReturnsNothing ? "  await" : "  const response = await").Append(" request.execute(").Append(function.Batch is null ? id : "'sys.sp_executesql'").Append(")\n");

        if (function.Results is not [])
            output.Append("  const sets = response.recordsets as unknown as readonly unknown[]\n");
        switch (function.Results)
        {
            case null:
                output.Append("  const result = await checked(").Append(SchemaModuleNames.ResultsSchema(name)).Append(", sets, ").Append(Fail(id, "the results")).Append(")\n");
                break;
            case []:
                break;
            case [var only]:
                output.Append("  const result = await checked(").Append(SchemaModuleNames.ResultsSchema(name)).Append(", ").Append(Read(only, 0)).Append(", ").Append(Fail(id, "the results")).Append(")\n");
                break;
            case var many:
                output.Append("  const [").Append(string.Join(", ", many.Select((_, i) => Result(i)))).Append("] = await checked(")
                    .Append(SchemaModuleNames.ResultsSchema(name)).Append(", [").Append(string.Join(", ", many.Select(Read))).Append("], ")
                    .Append(Fail(id, "the results")).Append(")\n");
                break;
        }

        if (function.HasOutput)
        {
            output.Append("  const output = await checked(").Append(SchemaModuleNames.OutputSchema(name)).Append(", response.output, ")
                .Append(Fail(id, "the OUTPUT parameters")).Append(")\n");
        }

        if (function.ReturnsResultAlone)
            output.Append("  return result\n");
        else if (!function.ReturnsNothing)
            output.Append("  return { ").Append(string.Join(", ", fields.Select(f => f.Value))).Append(" }\n");
        output.Append("}\n");
    }

    /// <summary>
    /// What a function hands back, by name: each result set, the <c>OUTPUT</c> parameters, and the return code when one other than
    /// 0 may come back. One result set is <c>result</c>, several are <c>result1</c>, <c>result2</c> and so on, in order. A function
    /// with only <c>result</c> returns it itself, and one with none returns nothing (<see cref="MssqlFunction.ReturnsResultAlone"/>).
    /// </summary>
    private static IEnumerable<(string Name, string Type, string Value)> Fields(MssqlFunction function)
    {
        var results = "types." + SchemaModuleNames.Results(function.Procedure.Name);
        switch (function.Results)
        {
            case null or [_]:
                yield return ("result", results, "result");
                break;
            case []:
                break;
            case var many:
                for (var i = 0; i < many.Count; i++)
                    yield return (Result(i), $"{results}[{i.ToString(CultureInfo.InvariantCulture)}]", Result(i));
                break;
        }

        if (function.HasOutput)
            yield return ("output", "types." + SchemaModuleNames.Output(function.Procedure.Name), "output");
        if (function.ReturnsCode)
            yield return ("returnValue", "number", "returnValue: response.returnValue");
    }

    private static string Result(int index) => "result" + (index + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>How the result set at <paramref name="index"/> is read, by its encoding.</summary>
    private static string Read(IrEncoding encoding, int index) =>
        (encoding == IrEncoding.Json ? "readJson" : "readRows") + "(sets[" + index.ToString(CultureInfo.InvariantCulture) + "])";

    private static string Fail(string id, string part) => "(issues) => new ResultError(" + id + ", " + Quoted(part) + ", issues)";

    /// <summary>
    /// How one parameter is bound: <c>input</c>, or <c>output</c> so that its value comes back; only when given, for one the
    /// caller may leave out (<see cref="Binding.OnlyWhenGiven"/>).
    /// </summary>
    private static string Bind(Binding binding)
    {
        var key = Quoted(binding.Key);
        var value = "p" + (IrIdentifiers.IsIdentifier(binding.Key) ? "." + binding.Key : "[" + key + "]");
        var call = $"request.{(binding.IsOutput ? "output" : "input")}({key}, {(binding.Type is { } type ? Type(type) + ", " : "")}{value})";
        return binding.OnlyWhenGiven ? $"if ({value} !== undefined) {call}" : call;
    }

    private static string Type(DriverType type) =>
        "sql." + type.Name + (type.Arguments.Count == 0 ? "" : "(" + string.Join(", ", type.Arguments.Select(a => a == "MAX" ? "sql.MAX" : a)) + ")");

    /// <summary>A string literal, in single quotes, escaped as JSON escapes it.</summary>
    private static string Quoted(string text) =>
        "'" + JsonEncodedText.Encode(text, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString().Replace("'", "\\'", StringComparison.Ordinal) + "'";

    /// <summary>Text made safe inside a comment: a procedure's id may hold anything a bracketed identifier may.</summary>
    private static string Comment(string text) =>
        text.Replace("*/", "*\\/", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
