using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>
/// Checks each parameter declared to hold JSON against the project: it must be a parameter of a procedure or function the project
/// defines, not <c>OUTPUT</c>, and of a string type (<c>char</c>, <c>varchar</c>, <c>nchar</c>, <c>nvarchar</c> or <c>json</c>,
/// directly or through an alias type), as a declared column must be
/// (docs/decisions/0143-a-parameter-s-json-may-be-declared.md).
/// </summary>
public static class ParameterDeclarationCheck
{
    /// <summary>A problem for each declaration the project can't hold; none when all can.</summary>
    public static IReadOnlyList<Diagnostic> Check(IEnumerable<ParameterDeclaration> parameters, DatabaseModel model)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(model);
        return
        [
            .. parameters
                .Select(declared => Find(declared.Owner, declared.Parameter, model) is (null, { } problem) ? ConfigCodes.ParameterNotDeclarable.At(declared.At, problem) : null)
                .OfType<Diagnostic>(),
        ];
    }

    /// <summary>
    /// The parameter <paramref name="parameter"/> of the procedure or function <paramref name="owner"/>, when it can hold a declaration;
    /// otherwise null and why, as a clause. A declaration in the config file and one in the SQL are held to the same rules.
    /// </summary>
    internal static (Parameter? Found, string? Problem) Find(ObjectName owner, string parameter, DatabaseModel model)
    {
        // SQL Server names a numbered procedure's parameters in sys.numbered_procedure_parameters, where no extended property reaches
        // (15135), and haruspex reads no numbered procedure (HAR1002), so nothing could read the declaration.
        if (owner.Name.Contains(';', StringComparison.Ordinal))
            return (null, $"{owner} is a numbered procedure, whose parameters can't be declared: haruspex reads no numbered procedure, and SQL Server takes no extended property on one's parameter.");

        IReadOnlyList<Parameter>? parameters = model.FindProcedure(owner)?.Parameters ?? model.FindFunction(owner)?.Parameters;
        if (parameters is null)
            return (null, $"there is no procedure or function {owner} in the project.");
        if (parameters.FirstOrDefault(p => p.Name.Equals(parameter, StringComparison.OrdinalIgnoreCase)) is not { } found)
            return (null, $"{owner} has no parameter {parameter}.");
        if (found.IsOutput)
            return (null, $"{owner}.{found.Name} is an OUTPUT parameter, whose value comes back as well as goes: declare the JSON a caller sends in a parameter that only takes it.");
        if (found.Type is not { } type || !ColumnDeclarationCheck.IsString(type, model))
            return (null, $"{owner}.{found.Name} is of the type {found.Type?.ToString() ?? "unknown"}, which can't hold JSON text: only a string type (char, varchar, nchar, nvarchar) or json can.");
        return (found, null);
    }
}
