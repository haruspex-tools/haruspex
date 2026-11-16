using Haruspex.Diagnostics;
using Haruspex.Emit.Mssql.Planning;
using Haruspex.Emit.Mssql.Printing;
using Haruspex.Ir;

namespace Haruspex.Emit.Mssql;

/// <summary>
/// Emits the database module's functions from an <see cref="IrDocument"/>: one TypeScript function for each procedure, which
/// checks its parameters, calls it through the mssql driver and checks what comes back, with the schemas a schema module (the
/// Zod emitter's) declares under <see cref="SchemaModuleNames"/>. It reads only the IR; it knows nothing about SQL.
/// </summary>
/// <remarks>
/// Two stages: <see cref="FunctionPlanner"/> decides each function's name, bindings and reads, and refuses a name it can't take;
/// <see cref="MssqlPrinter"/> writes them, and is the only stage that knows TypeScript and the driver. The values are checked
/// through Standard Schema, not through Zod's own methods, so the functions don't change with the validator that wrote the
/// schemas (<c>docs/decisions/0056-a-function-throws-three-kinds-of-failure.md</c>).
/// </remarks>
public static class MssqlEmitter
{
    /// <summary>
    /// The name of the function of the procedure named <paramref name="procedure"/> (its settled name): <c>GetOrder</c> is
    /// <c>getOrder</c>. The one name this emitter makes.
    /// </summary>
    public static string FunctionName(string procedure) => FunctionPlanner.FunctionName(procedure);

    /// <summary>Emits the module with the default options.</summary>
    /// <returns>The module's text; or every refusal (<see cref="MssqlCodes"/>).</returns>
    public static Outcome<string> Emit(IrDocument document) => Emit(document, new MssqlOptions());

    /// <summary>Emits the module.</summary>
    /// <returns>The module's text; or, when a function can't take its name, every refusal (<see cref="MssqlCodes"/>).</returns>
    public static Outcome<string> Emit(IrDocument document, MssqlOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        return FunctionPlanner.Plan(document, options).Select(functions => MssqlPrinter.Print(functions, options));
    }
}
