using System.Globalization;
using System.Xml.Linq;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>
/// Reads one package's elements into what the database's sources declare (<see cref="SchemaDeclarations"/>, the same a folder's
/// scripts fill), so that the passes after reading and the model are one for a folder and a package. Every element is read,
/// passed over for a reason given here, or counted in one note, so none is lost in silence.
/// </summary>
internal static class PackageReader
{
    /// <summary>
    /// Elements with no bearing on any shape haruspex claims, passed over without a word: security (users, roles, logins,
    /// permissions, row-level security, which filters rows but never changes a column), storage (filegroups, partitions,
    /// compression), documentation (extended properties), schemas, statistics and indexes' storage, and defaults (a
    /// column's <c>DEFAULT</c> decides what an insert writes when given nothing, not what a column can hold).
    /// </summary>
    private static readonly HashSet<string> Irrelevant = new(StringComparer.Ordinal)
    {
        "SqlSchema", "SqlUser", "SqlRole", "SqlRoleMembership", "SqlLogin", "SqlPermissionStatement", "SqlApplicationRole",
        "SqlSecurityPolicy", "SqlFilegroup", "SqlFile", "SqlPartitionFunction", "SqlPartitionScheme", "SqlColumnStoreIndex",
        "SqlStatistic", "SqlExtendedProperty", "SqlDefaultConstraint", "SqlFullTextCatalog", "SqlFullTextIndex",
        "SqlFullTextStopList", "SqlDatabaseCredential", "SqlMasterKey", "SqlCertificate", "SqlSymmetricKey",
        "SqlAsymmetricKey", ModelXml.SqlDatabaseOptions,
    };

    /// <summary>The parts of a table that live in elements of their own, read with it (<see cref="TableReader"/>).</summary>
    private static readonly HashSet<string> TableParts = new(StringComparer.Ordinal)
    {
        ModelXml.SqlCheckConstraint, ModelXml.SqlPrimaryKeyConstraint, ModelXml.SqlUniqueConstraint, ModelXml.SqlIndex, ModelXml.SqlForeignKeyConstraint,
    };

    private static readonly HashSet<string> Modules = new(StringComparer.Ordinal)
    {
        ModelXml.SqlProcedure, ModelXml.SqlScalarFunction, ModelXml.SqlInlineTableValuedFunction, ModelXml.SqlMultiStatementTableValuedFunction,
        ModelXml.SqlView, ModelXml.SqlDmlTrigger,
    };

    /// <summary>Reads the package's schema into <paramref name="into"/>.</summary>
    public static void Read(DacPackage package, SchemaDeclarations into)
    {
        var elements = package.Model.Elements(ModelXml.Name("Element")).ToList();
        var parts = elements.Where(e => TableParts.Contains(ModelXml.TypeOf(e))).ToLookup(Owner, StringComparer.Ordinal);
        var tables = new TableReader(package.Name, package.Built, into.Diagnostics);
        var modules = new List<XElement>();
        var notModelled = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            var type = ModelXml.TypeOf(element);
            switch (type)
            {
                case ModelXml.SqlTable:
                    var (table, keys) = tables.Read(element, [.. parts[ModelXml.NameOf(element) ?? ""]]);
                    into.Define(into.Tables, table.Name, table, table.Span.Start);
                    into.CascadingKeys.AddRange(keys);
                    break;
                case ModelXml.SqlUserDefinedDataType or ModelXml.SqlUserDefinedType:
                    var userType = UserType(element, package.Name);
                    into.Define(into.UserTypes, userType.Name, userType, userType.Span.Start);
                    break;
                case ModelXml.SqlSynonym when Synonym(element, package.Name, into.Diagnostics) is { } synonym:
                    into.Synonyms.TryAdd(synonym.Name, synonym);
                    break;
                case var module when Modules.Contains(module):
                    modules.Add(element);
                    break;
                case var _ when TableParts.Contains(type) || Irrelevant.Contains(type):
                    break;
                default:
                    notModelled[type] = notModelled.GetValueOrDefault(type) + 1;
                    break;
            }
        }

        ReadModules(modules, package.Name, into);
        if (notModelled.Count > 0)
        {
            var counts = string.Join(", ", notModelled.Select(n => $"{n.Value.ToString(CultureInfo.InvariantCulture)} {n.Key}"));
            into.Diagnostics.Add(SourceCodes.NotModelled.At(new FileLocation(package.Path), $"{package.Name} holds elements haruspex doesn't model, which are passed over: {counts}."));
        }
    }

    /// <summary>
    /// The database's settings, from the package's <c>SqlDatabaseOptions</c>; null when it has none, or names no collation. The
    /// compatibility level is written there when it differs from the provider's, and otherwise in the header.
    /// </summary>
    public static DatabaseSettings? Settings(DacPackage package)
    {
        var options = package.Model.Elements(ModelXml.Name("Element")).FirstOrDefault(e => ModelXml.TypeOf(e) == ModelXml.SqlDatabaseOptions);
        if (options is null || ModelXml.Property(options, ModelXml.Collation) is not { } collation)
            return null;
        var level = TypeSpecifier.Number(options, ModelXml.CompatibilityMode) ?? package.CompatibilityMode;
        if (level is not { } compatibility)
            return null;
        bool On(string name) => ModelXml.IsTrue(options, name);
        return new DatabaseSettings(
            collation,
            compatibility,
            On(ModelXml.IsAnsiNullsOn),
            On(ModelXml.IsAnsiNullDefaultOn),
            On(ModelXml.IsAnsiPaddingOn),
            On(ModelXml.IsAnsiWarningsOn),
            On(ModelXml.IsArithAbortOn),
            On(ModelXml.IsConcatNullYieldsNullOn),
            On(ModelXml.IsNumericRoundAbortOn),
            ModelXml.Property(options, ModelXml.IsQuotedIdentifierOn) != "False");
    }

    /// <summary>
    /// The modules, parsed in parallel (parsing is most of a large package's reading time) and added in the package's order,
    /// so the outcome doesn't depend on which finishes first.
    /// </summary>
    private static void ReadModules(List<XElement> modules, string package, SchemaDeclarations into)
    {
        var read = modules
            .AsParallel()
            .AsOrdered()
            .Select(element =>
            {
                var diagnostics = new List<Diagnostic>();
                var file = PackageLocation.File(package, ElementName.Object(ModelXml.Named(element)));
                return (Module: ModuleTextReader.Read(element, file, diagnostics), Diagnostics: diagnostics);
            })
            .ToList();
        foreach (var (module, diagnostics) in read)
        {
            into.Diagnostics.AddRange(diagnostics);
            switch (module)
            {
                case Procedure procedure:
                    into.Define(into.Procedures, procedure.Name, procedure, procedure.Span.Start);
                    break;
                case Function function:
                    into.Define(into.Functions, function.Name, function, function.Span.Start);
                    break;
                case View view:
                    if (!into.Views.TryGetValue(view.Name, out var definitions))
                        into.Views[view.Name] = definitions = [];
                    definitions.Add(new ViewDefinition(view, RedefiningStatement.CreateView));
                    break;
                case Trigger trigger:
                    into.Triggers.Add(trigger);
                    break;
                case null:
                    break;
            }
        }
    }

    /// <summary>The table a part of one names (a constraint's <c>DefiningTable</c>, an index's <c>IndexedObject</c>), as written.</summary>
    private static string Owner(XElement part) =>
        ModelXml.References(part, ModelXml.DefiningTable).Concat(ModelXml.References(part, ModelXml.IndexedObject))
            .Select(ModelXml.NameOf).FirstOrDefault() ?? "";

    /// <summary>
    /// An alias type, with its base type from its own element's properties and its nullability as declared (<c>IsNullable</c>
    /// false for NOT NULL, true for NULL, absent for neither); or a CLR type.
    /// </summary>
    private static UserType UserType(XElement element, string package)
    {
        var name = ElementName.Object(ModelXml.Named(element));
        var span = PackageLocation.Whole(PackageLocation.File(package, name), 1);
        if (ModelXml.TypeOf(element) == ModelXml.SqlUserDefinedType || TypeSpecifier.Read(element) is not { } baseType)
            return new ClrType(name, span);
        var nullability = ModelXml.Property(element, ModelXml.IsNullable) switch
        {
            "False" => DeclaredNullability.NotNull,
            "True" => DeclaredNullability.Null,
            _ => DeclaredNullability.NotDeclared,
        };
        return new AliasType(name, span, baseType, nullability);
    }

    /// <summary>A synonym, with the name it stands for parsed from its script as written; null, reported, when it doesn't parse.</summary>
    private static Synonym? Synonym(XElement element, string package, List<Diagnostic> diagnostics)
    {
        var name = ElementName.Object(ModelXml.Named(element));
        var span = PackageLocation.Whole(PackageLocation.File(package, name), 1);
        using var reader = new StringReader(ModelXml.Property(element, ModelXml.ForObjectScript) ?? "");
        var target = new TSql170Parser(true).ParseSchemaObjectName(reader, out var errors);
        foreach (var error in errors)
            diagnostics.Add(new Diagnostic(SourceCodes.ParseError, error.Message, new SourceLocation(span.File, error.Line, error.Column)));
        return errors.Count > 0 ? null : new Synonym(name, span, new SynonymTarget(
            target.ServerIdentifier?.Value, target.DatabaseIdentifier?.Value, target.SchemaIdentifier?.Value, target.BaseIdentifier.Value, Syntax.FragmentText.OneLine(target)));
    }
}
