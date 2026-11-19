using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// A document split into modules for an emitter that writes a file per module: each object in the module it is given, and each
/// named type where the objects that reach it are. A type the objects of one module alone reach is that module's; one that the
/// objects of two or more reach, or of none, is <see cref="Shared"/>, written once in a shared file that the modules import
/// from, since modules never import each other (<c>docs/decisions/0074-a-config-may-split-its-output-into-modules.md</c>).
/// </summary>
/// <remarks>
/// Which module an object belongs to is the config file's to say; the IR stays one document and records no module, so a
/// module is a way of writing the document out, not a claim about the database. Like <see cref="IrTypeGraph"/>, this is about
/// the document's types, so every emitter asks it the same question and gets the same answer.
/// </remarks>
public sealed class IrModules
{
    /// <summary>The name of the shared module, which no module of objects may take.</summary>
    public const string Shared = "shared";

    private readonly Dictionary<string, string> moduleOf;
    private readonly Dictionary<string, IReadOnlySet<string>> reaching;
    private readonly IrDocument document;

    private IrModules(IrDocument document, Dictionary<string, string> moduleOf, Dictionary<string, IReadOnlySet<string>> reaching)
    {
        this.document = document;
        this.moduleOf = moduleOf;
        this.reaching = reaching;
        Names = [.. moduleOf.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>The modules that hold an object, in ordinal order; never <see cref="Shared"/>.</summary>
    public EquatableArray<string> Names { get; }

    /// <summary>The document split by <paramref name="moduleOf"/>, which gives each object's module by its id.</summary>
    /// <exception cref="ArgumentException">An object is given no module, or the shared module's name.</exception>
    public static IrModules Of(IrDocument document, IReadOnlyDictionary<string, string> moduleOf)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(moduleOf);
        var modules = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var obj in document.Objects)
        {
            if (!moduleOf.TryGetValue(obj.Id, out var module))
                throw new ArgumentException($"The object {obj.Id} is in no module.", nameof(moduleOf));
            if (string.Equals(module, Shared, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"The object {obj.Id} is put in the module {module}, which is the shared module's name.", nameof(moduleOf));
            modules[obj.Id] = module;
        }

        var graph = IrTypeGraph.Of(document);
        var reaching = document.Types.Keys.ToDictionary(name => name, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var obj in document.Objects)
        {
            var types = obj.Parameters.Select(p => p.Type).Concat(obj.Results?.Select(r => r.Type) ?? []);
            foreach (var name in graph.ReachableFrom(types))
                reaching[name].Add(modules[obj.Id]);
        }

        return new IrModules(document, modules, reaching.ToDictionary(r => r.Key, r => (IReadOnlySet<string>)r.Value, StringComparer.Ordinal));
    }

    /// <summary>The module <paramref name="obj"/> is in.</summary>
    public string Of(IrObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return moduleOf[obj.Id];
    }

    /// <summary>The objects of <paramref name="module"/>, by id.</summary>
    public IEnumerable<IrObject> ObjectsOf(string module) =>
        document.Objects.Where(o => moduleOf[o.Id] == module).OrderBy(o => o.Id, StringComparer.Ordinal);

    /// <summary>The modules whose objects reach the named type <paramref name="type"/>, through references at any depth.</summary>
    public IReadOnlySet<string> Reaching(string type) => reaching[type];

    /// <summary>Where the named type <paramref name="type"/> is written: the one module that reaches it, or else <see cref="Shared"/>.</summary>
    public string HomeOf(string type) => HomeOf([type]);

    /// <summary>
    /// Where a declaration that several named types rest on is written, such as a shape they share: the one module that reaches
    /// any of them, or else <see cref="Shared"/>.
    /// </summary>
    public string HomeOf(IEnumerable<string> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var modules = types.SelectMany(Reaching).Distinct(StringComparer.Ordinal).Take(2).ToList();
        return modules is [var only] ? only : Shared;
    }
}
