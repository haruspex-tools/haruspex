namespace Haruspex.SqlServer.Model;

/// <summary>
/// A body that is a method of a .NET assembly (<c>AS EXTERNAL NAME assembly.class.method</c>), whose code is not in the
/// project.
/// </summary>
/// <param name="Assembly">The assembly's name in the database.</param>
/// <param name="Class">The class, as written.</param>
/// <param name="Method">The method, or null when the name gives none.</param>
public sealed record ClrBody(string Assembly, string Class, string? Method) : ModuleBody;
