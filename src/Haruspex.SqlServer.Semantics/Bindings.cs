using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// What every body of one model binds to, kept: each query bound once for each set of names in force, and each view's and
/// inline function's columns listed once. A <see cref="Binder"/> for each body draws on it, so a view's query bound for its
/// columns where a body names it is the same bound query its own reading uses, and a statement read again (a loop's next
/// pass, a recursive CTE's next step, another branch) is not bound again.
/// </summary>
/// <remarks>
/// This is a memo table, the one place binding keeps anything: binding is a function of a fragment, the scope it is read
/// inside and the names in force (<see cref="NameEnvironment"/>), so a result kept for those three is the result. It grows as
/// bodies are read and is dropped with the model's inference.
/// </remarks>
/// <param name="model">The model every body is bound against.</param>
public sealed class Bindings(DatabaseModel model)
{
    private readonly Dictionary<Key, object> bound = [];
    private readonly Dictionary<Module, IReadOnlyList<string>?> columns = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Module> listing = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, BodyDeclarations> declarations = new(ReferenceEqualityComparer.Instance);

    /// <summary>The model.</summary>
    public DatabaseModel Model { get; } = model ?? throw new ArgumentNullException(nameof(model));

    /// <summary>The binder of one body.</summary>
    /// <param name="source">Where the object whose body it is was read; positions in the body are in its file.</param>
    public Binder For(SourceSpan source) => new(this, source);

    /// <summary>
    /// What a body declares (<see cref="BodyDeclarations"/>), read once for each body, so that the names in force there, which
    /// hold them, compare equal wherever they are made.
    /// </summary>
    /// <param name="parameters">The procedure's or function's parameters.</param>
    /// <param name="body">Its statements; null for a body of one expression or query, which declares nothing.</param>
    /// <param name="returnTable">A multi-statement function's return table, or null.</param>
    /// <param name="source">Where the object whose body it is was read.</param>
    public BodyDeclarations Declarations(IReadOnlyList<Parameter> parameters, StatementList? body, DeclaredTable? returnTable, SourceSpan source)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var key = body ?? (object)parameters;
        if (!declarations.TryGetValue(key, out var found))
        {
            found = body is null ? BodyDeclarations.Of(parameters) : BodyDeclarations.Read(parameters, body, returnTable, source);
            declarations[key] = found;
        }

        return found;
    }

    /// <summary>What was bound for a fragment read inside <paramref name="outer"/> with <paramref name="names"/>, binding it the first time.</summary>
    internal T Once<T>(TSqlFragment fragment, QueryScope? outer, NameEnvironment names, Func<T> bind)
        where T : class
    {
        var key = new Key(fragment, outer, names);
        if (bound.TryGetValue(key, out var found))
            return (T)found;
        var result = bind();
        bound[key] = result;
        return result;
    }

    /// <summary>
    /// A view's or inline function's columns, listed once; null when they can't be listed, and for one whose definition reads
    /// itself, which SQL Server refuses.
    /// </summary>
    internal IReadOnlyList<string>? Columns(Module module, Func<IReadOnlyList<string>?> list)
    {
        if (columns.TryGetValue(module, out var found))
            return found;
        if (!listing.Add(module))
            return null;
        try
        {
            var result = list();
            columns[module] = result;
            return result;
        }
        finally
        {
            listing.Remove(module);
        }
    }

    /// <summary>A fragment by reference, the scope it is read inside by reference, and the names in force by value.</summary>
    private readonly record struct Key(TSqlFragment Fragment, QueryScope? Outer, NameEnvironment Names)
    {
        public bool Equals(Key other) =>
            ReferenceEquals(Fragment, other.Fragment) && ReferenceEquals(Outer, other.Outer) && Names.Equals(other.Names);

        public override int GetHashCode() => HashCode.Combine(
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Fragment),
            Outer is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Outer),
            Names);
    }
}
