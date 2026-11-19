using System.Text.Json;
using System.Text.RegularExpressions;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>
/// Reads <c>haruspex.json</c>: finds it, parses it (JSON with comments and trailing commas, as tsconfig.json allows) and
/// checks every setting, reporting each problem with the path inside the file and its line. The checks are the same as
/// the published JSON Schema's (<c>schemas/haruspex.schema.json</c>), plus what a schema can't say: that a pattern or a
/// regular expression is well formed. An unknown key is an error, never ignored: a misspelt setting would otherwise
/// silently do nothing.
/// </summary>
public static class ConfigReader
{
    /// <summary>How long one naming replacement may run on one name; regular expressions come from users.</summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The config file that applies in <paramref name="directory"/>: <c>haruspex.json</c> there or in the nearest folder
    /// above it, or null when there is none.
    /// </summary>
    public static string? Find(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        for (var folder = new DirectoryInfo(Path.GetFullPath(directory)); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Join(folder.FullName, HaruspexConfig.FileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>Reads and checks the file at <paramref name="path"/>.</summary>
    /// <param name="path">The file.</param>
    /// <param name="reserved">The words the generated code's language keeps, which no name in the file may be; none if null.</param>
    /// <returns>The config, with any warnings; or, when it has an error, every problem found.</returns>
    public static Outcome<HaruspexConfig> Read(string path, ReservedWords? reserved = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Failed<HaruspexConfig>([ConfigCodes.Unreadable.At(new FileLocation(path), $"cannot read the config file: {e.Message}")]);
        }

        // Never null: the full path of the file read above, which is never a root.
        return Read(bytes, path, Path.GetDirectoryName(Path.GetFullPath(path))!, reserved);
    }

    /// <summary>Reads and checks config text.</summary>
    /// <param name="utf8">The file's bytes.</param>
    /// <param name="file">The file's name, for problems.</param>
    /// <param name="directory">The folder its relative paths are relative to.</param>
    /// <param name="reserved">The words the generated code's language keeps, which no name in the file may be; none if null.</param>
    /// <returns>The config, with any warnings; or, when it has an error, every problem found.</returns>
    public static Outcome<HaruspexConfig> Read(byte[] utf8, string file, string directory, ReservedWords? reserved = null)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(directory);
        return ConfigJson.Parse(utf8, file).Then(root =>
        {
            var reader = new Checker(file, directory, reserved ?? ReservedWords.None);
            var config = reader.Root(root, directory);

            // A warning (a JSON Schema keyword in a declaration that is not checked) leaves the config usable.
            return Outcome.Of(config, reader.Problems);
        });
    }

    private sealed class Checker(string file, string directory, ReservedWords reserved)
    {
        public List<Diagnostic> Problems { get; } = [];

        public HaruspexConfig Root(JsonValue value, string directory)
        {
            var config = new HaruspexConfig { File = file, Directory = directory, Reserved = reserved };
            if (Object(value, "") is not { } root)
                return config;

            // serverOnly is about the functions' file, which "emit" may name after it.
            JsonValue? serverOnlyAt = null;

            foreach (var member in root.Members)
            {
                var path = Child("", member.Name);
                switch (member.Name)
                {
                    case "$schema":
                        String(member.Value, path);
                        break;
                    case "source":
                        if (NonEmpty(member.Value, path) is { } source)
                            config = config with { Source = Path.GetFullPath(Path.Combine(directory, source)) };
                        break;
                    case "baseline":
                        if (NonEmpty(member.Value, path) is { } baseline)
                            config = config with { Baseline = Path.GetFullPath(Path.Combine(directory, baseline)) };
                        break;
                    case "references":
                        config = config with { References = [.. Folders(member.Value, path, directory)] };
                        break;
                    case "collation":
                        if (NonEmpty(member.Value, path) is { } collation)
                        {
                            if (collation.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                                config = config with { Collation = collation };
                            else
                                Report(ConfigCodes.WrongKind, member.Value, path, $"is \"{collation}\", which is no collation's name: a name such as Latin1_General_100_CI_AS has only letters, digits and underscores.");
                        }

                        break;
                    case "queries":
                        config = config with { Queries = [.. Queries(member.Value, path)] };
                        break;
                    case "out":
                        if (NonEmpty(member.Value, path) is { } output)
                            config = config with { Out = Path.GetFullPath(Path.Combine(directory, output)) };
                        break;
                    case "split":
                        config = config with { Split = Split(member.Value, path) };
                        break;
                    case "schemas":
                        config = config with { Schemas = [.. Schemas(member.Value, path)] };
                        break;
                    case "objects":
                        if (Choice(member.Value, path, "strip", "strict") is { } objects)
                            config = config with { Objects = objects == "strict" ? ObjectsPolicy.Strict : ObjectsPolicy.Strip };
                        break;
                    case "emit":
                        if (Emitters(member.Value, path) is { } emitters)
                            config = config with { Emitters = emitters };
                        break;
                    case "naming":
                        var naming = Naming(member.Value, path);
                        config = config with { Naming = new NamingRules(naming.Replace, naming.Case ?? NamingCase.Pascal), NamingAt = naming.At };
                        break;
                    case "procedures":
                        config = config with { Procedures = [.. Procedures(member.Value, path)] };
                        break;
                    case "session":
                        config = config with { Session = Session(member.Value, path) };
                        break;
                    case "check":
                        config = config with { CheckUnknowns = Check(member.Value, path) };
                        break;
                    case "acknowledge":
                        config = config with { Acknowledged = [.. Acknowledge(member.Value, path)] };
                        break;
                    case "serverOnly":
                        if (member.Value is JsonOtherValue { Kind: JsonTokenType.True or JsonTokenType.False } serverOnly)
                        {
                            config = config with { ServerOnly = serverOnly.Kind == JsonTokenType.True };
                            serverOnlyAt = member.Value;
                        }
                        else
                        {
                            Report(ConfigCodes.WrongKind, member.Value, path, "must be true or false.");
                        }

                        break;
                    default:
                        Unknown(member, path, "$schema, source, baseline, references, collation, queries, out, split, schemas, objects, emit, serverOnly, naming, procedures, session, check, acknowledge");
                        break;
                }
            }

            if (serverOnlyAt is not null && !config.Emitters.Contains(Emitter.Mssql))
                Report(ConfigCodes.NeedsAnother, serverOnlyAt, "serverOnly", "is about the functions' file, which \"emit\" writes when it names \"mssql\"; it names none here.");
            return config;
        }

        /// <summary>
        /// How the output is split into modules: <c>"schema"</c>, or an object of modules, each an array of the schemas it holds.
        /// Each module's name names its files, so it is checked as one (<see cref="ModuleNames"/>), and two names differing only in
        /// case are refused; a schema is in one module, compared ignoring case as SQL Server compares schema names. Null when the
        /// value is wrong, which is reported.
        /// </summary>
        private ModuleSplit? Split(JsonValue value, string path)
        {
            var at = At(value, path);
            if (value is JsonStringValue { Value: "schema" })
                return new ModuleSplit(null, at);
            if (value is not JsonObjectValue block)
            {
                Report(ConfigCodes.WrongKind, value, path, "must be \"schema\", for a module per schema, or an object naming each module and the schemas it holds, such as { \"sales\": [\"sales\", \"shop\"] }.");
                return null;
            }

            var modules = new List<SplitModule>();
            var schemaIn = new Dictionary<string, SplitModule>(StringComparer.OrdinalIgnoreCase);
            var valid = true;
            foreach (var member in block.Members)
            {
                var modulePath = Child(path, member.Name);
                var moduleAt = new JsonLocation(file, modulePath, member.Line, member.Column);
                var problem = ModuleNames.Problem(member.Name)
                    ?? (modules.FirstOrDefault(m => string.Equals(m.Name, member.Name, StringComparison.OrdinalIgnoreCase)) is { } same
                        ? $"the module {same.Name}, at line {same.At.Line}, has the name in another case, and a file system that ignores case would hold one file for both."
                        : null);
                if (problem is not null)
                {
                    Problems.Add(ConfigCodes.BadModuleName.At(moduleAt, $"'{member.Name}' can't name a module: {problem}"));
                    valid = false;
                    continue;
                }

                if (member.Value is not JsonArrayValue array)
                {
                    Report(ConfigCodes.WrongKind, member.Value, modulePath, "must be an array of the schemas the module holds, such as [\"sales\", \"shop\"]; [] for a module of query files alone.");
                    valid = false;
                    continue;
                }

                var module = new SplitModule(member.Name, [], moduleAt);
                var schemas = new List<string>();
                for (var i = 0; i < array.Items.Count; i++)
                {
                    var itemPath = Index(modulePath, i);
                    if (NonEmpty(array.Items[i], itemPath) is not { } schema)
                    {
                        valid = false;
                    }
                    else if (schemaIn.TryGetValue(schema, out var earlier))
                    {
                        Report(ConfigCodes.SchemaInTwoModules, array.Items[i], itemPath,
                            $"the schema {schema} is in the module {earlier.Name} already, at line {earlier.At.Line}; a schema's procedures are in one module. Schema names are compared ignoring case, as SQL Server compares them.");
                        valid = false;
                    }
                    else
                    {
                        schemaIn[schema] = module;
                        schemas.Add(schema);
                    }
                }

                modules.Add(module with { Schemas = [.. schemas] });
            }

            return valid ? new ModuleSplit([.. modules], at) : null;
        }

        /// <summary>
        /// The options the callers' connections set, each true or false; one left out is not known. Only the options a rule
        /// reads are settings (<see cref="SessionOptions.Names"/>): any other, a database's default or a driver's, is unknown
        /// rather than silently ignored (<c>docs/decisions/0038-the-callers-session-is-declared.md</c>).
        /// </summary>
        private SessionOptions Session(JsonValue value, string path)
        {
            var session = SessionOptions.NotKnown;
            if (Object(value, path) is not { } block)
                return session;
            foreach (var member in block.Members)
            {
                var memberPath = Child(path, member.Name);
                if (!SessionOptions.Names.Contains(member.Name, StringComparer.Ordinal))
                    Unknown(member, memberPath, string.Join(", ", SessionOptions.Names));
                else if (member.Value is JsonOtherValue { Kind: JsonTokenType.True or JsonTokenType.False } flag)
                    session = session.With(member.Name, flag.Kind == JsonTokenType.True);
                else
                    Report(ConfigCodes.WrongKind, member.Value, memberPath, "must be true or false; leave the option out when it is not known.");
            }

            return session;
        }

        /// <summary>
        /// What is written beside the IR: at least one emitter, each named once, kept in the file's order. Null, leaving the
        /// default, when the value is wrong, which is reported.
        /// </summary>
        private EquatableArray<Emitter>? Emitters(JsonValue value, string path)
        {
            const string Expected = "must be an array of what to write beside the IR: any of \"zod\", \"json-schema\", \"mssql\" and \"mapping\".";
            if (value is not JsonArrayValue array)
            {
                Report(ConfigCodes.WrongKind, value, path, Expected);
                return null;
            }

            if (array.Items.Count == 0)
            {
                Report(ConfigCodes.WrongKind, value, path, "must name at least one emitter; with none, generate would write the IR alone. Use [\"zod\"], the default, or leave it out.");
                return null;
            }

            var emitters = new List<Emitter>();
            var valid = true;
            for (var i = 0; i < array.Items.Count; i++)
            {
                var item = array.Items[i];
                switch (Choice(item, Index(path, i), "zod", "json-schema", "mssql", "mapping"))
                {
                    case null:
                        valid = false;
                        break;
                    case var name when emitters.Contains(Of(name)):
                        Report(ConfigCodes.WrongKind, item, Index(path, i), $"names \"{name}\" again; each emitter is listed once.");
                        valid = false;
                        break;
                    case var name:
                        emitters.Add(Of(name));
                        break;
                }
            }

            // The functions import the Zod module's schemas, so they can't be written without it.
            if (valid && emitters.Contains(Emitter.Mssql) && !emitters.Contains(Emitter.Zod))
            {
                Report(ConfigCodes.NeedsAnother, value, path, "names \"mssql\" without \"zod\": the functions import the Zod module's schemas, so name both.");
                valid = false;
            }

            // The helper works on the types the Zod module declares; without them it has nothing to account for.
            if (valid && emitters.Contains(Emitter.Mapping) && !emitters.Contains(Emitter.Zod))
            {
                Report(ConfigCodes.NeedsAnother, value, path, "names \"mapping\" without \"zod\": the mapping helper works on the types the Zod module declares, so name both.");
                valid = false;
            }

            return valid ? [.. emitters] : null;

            static Emitter Of(string name) => name switch
            {
                "zod" => Emitter.Zod,
                "json-schema" => Emitter.JsonSchema,
                "mssql" => Emitter.Mssql,
                _ => Emitter.Mapping,
            };
        }

        /// <summary>
        /// The warnings acknowledged: each an object with its <c>code</c> (a warning's, <c>HAR</c> and four digits), its
        /// <c>subject</c> and <c>why</c>, all three required, so a warning is never acknowledged by its code alone, nor without a
        /// reason; and each once.
        /// </summary>
        private List<Acknowledgement> Acknowledge(JsonValue value, string path)
        {
            var acknowledged = new List<Acknowledgement>();
            if (value is not JsonArrayValue array)
            {
                Report(ConfigCodes.WrongKind, value, path, "must be an array of warnings acknowledged, each { \"code\", \"subject\", \"why\" }.");
                return acknowledged;
            }

            for (var i = 0; i < array.Items.Count; i++)
            {
                var itemPath = Index(path, i);
                if (Object(array.Items[i], itemPath) is not { } entry)
                    continue;
                string? code = null, subject = null, why = null;
                foreach (var member in entry.Members)
                {
                    var memberPath = Child(itemPath, member.Name);
                    switch (member.Name)
                    {
                        case "code":
                            code = NonEmpty(member.Value, memberPath);
                            if (code is not null && !(code.Length == 7 && code.StartsWith("HAR", StringComparison.Ordinal) && code[3..].All(char.IsAsciiDigit)))
                            {
                                Report(ConfigCodes.WrongKind, member.Value, memberPath, $"is \"{code}\", which is no diagnostic's code: a code is HAR and four digits, such as HAR1008.");
                                code = null;
                            }

                            break;
                        case "subject":
                            subject = NonEmpty(member.Value, memberPath);
                            break;
                        case "why":
                            why = NonEmpty(member.Value, memberPath);
                            break;
                        default:
                            Unknown(member, memberPath, "code, subject, why");
                            break;
                    }
                }

                var at = new JsonLocation(file, itemPath, array.Items[i].Line, array.Items[i].Column);
                var missing = new[] { ("code", code), ("subject", subject), ("why", why) }.Where(m => m.Item2 is null && !entry.Members.Any(e => e.Name == m.Item1)).Select(m => $"\"{m.Item1}\"").ToList();
                if (missing.Count > 0)
                {
                    Problems.Add(ConfigCodes.WrongKind.At(at, $"an acknowledgement needs {string.Join(" and ", missing)}: a warning is acknowledged by its code and its subject together, never by its code alone, and says why."));
                    continue;
                }

                if (code is null || subject is null || why is null)
                    continue;
                if (acknowledged.FirstOrDefault(a => a.Code == code && a.Subject == subject) is { } earlier)
                {
                    Problems.Add(ConfigCodes.WrongKind.At(at, $"acknowledges {code} for \"{subject}\" again; it is acknowledged at line {earlier.At.Line}."));
                    continue;
                }

                acknowledged.Add(new Acknowledgement(code, subject, why, at));
            }

            return acknowledged;
        }

        /// <summary>What <c>haruspex check</c> does beyond comparing: today, whether an unknown fails it.</summary>
        private CheckUnknowns Check(JsonValue value, string path)
        {
            var unknowns = CheckUnknowns.Report;
            if (Object(value, path) is not { } block)
                return unknowns;
            foreach (var member in block.Members)
            {
                var memberPath = Child(path, member.Name);
                if (member.Name != "unknowns")
                    Unknown(member, memberPath, "unknowns");
                else if (Choice(member.Value, memberPath, "report", "fail") is { } choice)
                    unknowns = choice == "fail" ? CheckUnknowns.Fail : CheckUnknowns.Report;
            }

            return unknowns;
        }

        private List<SchemaSettings> Schemas(JsonValue value, string path)
        {
            var schemas = new List<SchemaSettings>();
            if (Object(value, path) is not { } members)
                return schemas;

            foreach (var member in members.Members)
            {
                var schemaPath = Child(path, member.Name);
                var at = new JsonLocation(file, schemaPath, member.Line, member.Column);
                if (member.Name.Length == 0)
                {
                    Problems.Add(ConfigCodes.WrongKind.At(at, "a schema's name must not be empty."));
                    continue;
                }

                if (schemas.FirstOrDefault(s => string.Equals(s.Name, member.Name, StringComparison.OrdinalIgnoreCase)) is { } earlier)
                {
                    Problems.Add(ConfigCodes.SchemaTwice.At(at,
                        $"the schema \"{earlier.Name}\" already has a block, at line {earlier.At.Line}; schema names are compared ignoring case, as SQL Server compares them. Merge the two."));
                    continue;
                }

                if (Object(member.Value, schemaPath) is not { } block)
                    continue;

                var schema = new SchemaSettings(member.Name, at);
                JsonMember? exclude = null;
                foreach (var setting in block.Members)
                {
                    var settingPath = Child(schemaPath, setting.Name);
                    switch (setting.Name)
                    {
                        case "include":
                            schema = schema with { Include = [.. Patterns(setting.Value, settingPath, requireOne: true)] };
                            break;
                        case "exclude":
                            exclude = setting;
                            schema = schema with { Exclude = [.. Patterns(setting.Value, settingPath, requireOne: false)] };
                            break;
                        case "prefix":
                            schema = schema with { Prefix = String(setting.Value, settingPath) ?? "" };
                            break;
                        case "naming":
                            schema = schema with { Naming = Naming(setting.Value, settingPath) };
                            break;
                        default:
                            Unknown(setting, settingPath, "include, exclude, prefix, naming");
                            break;
                    }
                }

                // Without an include nothing in the schema is selected, so an exclude there could only ever be stale.
                if (exclude is not null && schema.Include is null)
                {
                    Problems.Add(ConfigCodes.ExcludeWithoutInclude.At(new JsonLocation(file, Child(schemaPath, "exclude"), exclude.Line, exclude.Column),
                        "does nothing without an \"include\" in the same block, since nothing in the schema is selected. Add the include, or remove the exclude."));
                }

                schemas.Add(schema);
            }

            return schemas;
        }

        /// <summary>
        /// The folders <c>references</c> names, each resolved against the config file's folder, in order: where referenced DACPACs
        /// are looked for after the folder of the package that refers to them, or where a project's build takes them from.
        /// </summary>
        private List<string> Folders(JsonValue value, string path, string directory)
        {
            var folders = new List<string>();
            if (value is not JsonArrayValue array)
            {
                Report(ConfigCodes.WrongKind, value, path, "must be an array of folders to look for referenced DACPACs in, such as \"../shared/bin/Release\".");
                return folders;
            }

            for (var i = 0; i < array.Items.Count; i++)
            {
                if (NonEmpty(array.Items[i], Index(path, i)) is not { } folder)
                    continue;
                var full = Path.GetFullPath(Path.Combine(directory, folder));
                if (Directory.Exists(full))
                    folders.Add(full);
                else
                    Report(ConfigCodes.NoSuchFolder, array.Items[i], Index(path, i), $"\"{folder}\" is not a folder, so no referenced DACPAC can be found in it.");
            }

            return folders;
        }

        /// <summary>
        /// The query files' entries: one, or an array of them, each a folder, a file or a pattern relative to the config file's
        /// folder and inside it, since a query file belongs to the module whose folder holds it.
        /// </summary>
        private List<QueryPattern> Queries(JsonValue value, string path)
        {
            const string Expected = "must name the application's query files: a folder, a file or a pattern such as \"queries/**/*.sql\", or an array of them.";
            var entries = value switch
            {
                JsonStringValue => [(value, path)],
                JsonArrayValue array when array.Items.Count > 0 => array.Items.Select((item, i) => (item, Index(path, i))).ToList(),
                _ => null,
            };
            if (entries is null)
            {
                Report(ConfigCodes.WrongKind, value, path, Expected);
                return [];
            }

            var patterns = new List<QueryPattern>();
            foreach (var (item, itemPath) in entries)
            {
                if (NonEmpty(item, itemPath) is not { } text)
                    continue;
                var entry = text.Replace('\\', '/');
                if (Path.IsPathRooted(entry) || entry.Split('/').Contains(".."))
                {
                    Report(ConfigCodes.QueriesOutside, item, itemPath, $"\"{text}\" reaches outside the folder of the config file; a query file belongs to the module whose folder holds it, so name only files below this one.");
                    continue;
                }

                // "./queries" is "queries", and "." the config file's folder itself.
                while (entry.StartsWith("./", StringComparison.Ordinal))
                    entry = entry[2..];
                patterns.Add(new QueryPattern(entry.TrimEnd('/') is "" or "." ? "." : entry.TrimEnd('/'), At(item, itemPath)));
            }

            return patterns;
        }

        private List<NamePatternEntry> Patterns(JsonValue value, string path, bool requireOne)
        {
            var patterns = new List<NamePatternEntry>();
            if (value is not JsonArrayValue array)
            {
                Report(ConfigCodes.WrongKind, value, path, "must be an array of patterns matching the schema's procedures by name, such as \"get_*\".");
                return patterns;
            }

            if (requireOne && array.Items.Count == 0)
                Report(ConfigCodes.WrongKind, value, path, "must list at least one pattern; with none, nothing is selected. Use \"*\" to select every procedure of the schema.");

            for (var i = 0; i < array.Items.Count; i++)
            {
                var itemPath = Index(path, i);
                if (String(array.Items[i], itemPath) is not { } text)
                    continue;
                if (NamePattern.TryParse(text, out var pattern, out var problem))
                    patterns.Add(new NamePatternEntry(pattern, At(array.Items[i], itemPath)));
                else
                    Report(ConfigCodes.BadPattern, array.Items[i], itemPath, problem);
            }

            return patterns;
        }

        /// <summary>A <c>naming</c> object, top-level or a schema's: the case is null when it doesn't say.</summary>
        private SchemaNaming Naming(JsonValue value, string path)
        {
            var naming = new SchemaNaming([], null) { At = At(value, path) };
            if (Object(value, path) is not { } members)
                return naming;

            foreach (var member in members.Members)
            {
                var memberPath = Child(path, member.Name);
                switch (member.Name)
                {
                    case "replace":
                        naming = naming with { Replace = [.. Replacements(member.Value, memberPath)] };
                        break;
                    case "case":
                        if (Choice(member.Value, memberPath, "pascal", "camel", "preserve") is { } style)
                        {
                            naming = naming with
                            {
                                Case = style switch
                                {
                                    "camel" => NamingCase.Camel,
                                    "preserve" => NamingCase.Preserve,
                                    _ => NamingCase.Pascal,
                                },
                            };
                        }

                        break;
                    default:
                        Unknown(member, memberPath, "replace, case");
                        break;
                }
            }

            return naming;
        }

        private List<NamingReplacement> Replacements(JsonValue value, string path)
        {
            var replacements = new List<NamingReplacement>();
            if (value is not JsonArrayValue array)
            {
                Report(ConfigCodes.WrongKind, value, path, "must be an array of { \"pattern\": \"…\", \"with\": \"…\" }.");
                return replacements;
            }

            for (var i = 0; i < array.Items.Count; i++)
            {
                var itemPath = Index(path, i);
                if (Object(array.Items[i], itemPath) is not { } rule)
                    continue;

                Regex? regex = null;
                string? with = null;
                foreach (var member in rule.Members)
                {
                    var memberPath = Child(itemPath, member.Name);
                    switch (member.Name)
                    {
                        case "pattern":
                            if (NonEmpty(member.Value, memberPath) is { } text)
                            {
                                try
                                {
                                    // NonBacktracking: the expression comes from a user, so matching stays linear in the
                                    // name's length. It has no backreferences or lookarounds, which names don't need.
                                    regex = new Regex(text, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, RegexTimeout);
                                }
                                catch (Exception e) when (e is ArgumentException or NotSupportedException)
                                {
                                    Report(ConfigCodes.BadRegex, member.Value, memberPath, $"is not a regular expression haruspex can use: {e.Message}");
                                }
                            }

                            break;
                        case "with":
                            with = String(member.Value, memberPath);
                            break;
                        default:
                            Unknown(member, memberPath, "pattern, with");
                            break;
                    }
                }

                if (!rule.Members.Any(m => m.Name == "pattern"))
                    Report(ConfigCodes.NeedsAnother, rule, itemPath, "needs \"pattern\", the regular expression to replace.");
                if (!rule.Members.Any(m => m.Name == "with"))
                    Report(ConfigCodes.NeedsAnother, rule, itemPath, "needs \"with\", the replacement (\"\" to remove the match).");
                if (regex is not null && with is not null)
                    replacements.Add(new NamingReplacement(regex, with, At(rule, itemPath)));
            }

            return replacements;
        }

        private List<ProcedureOverride> Procedures(JsonValue value, string path)
        {
            var procedures = new List<ProcedureOverride>();
            if (Object(value, path) is not { } members)
                return procedures;

            foreach (var member in members.Members)
            {
                if (Procedure(member, Child(path, member.Name)) is { } procedure)
                    procedures.Add(procedure);
            }

            return procedures;
        }

        /// <summary>The settings a procedure's block holds, as read.</summary>
        private sealed record ProcedureSettings(string? Name, JsonLocation? NameAt, EquatableArray<PathOverride> Paths, JsonMember? Results, string? Why)
        {
            /// <summary>The names of its result sets, in order, when given and each usable.</summary>
            public EquatableArray<string>? Sets { get; init; }

            /// <summary>Where they are written.</summary>
            public JsonLocation? SetsAt { get; init; }
        }

        /// <summary>One procedure's overrides (<c>procedures["s.p"]</c>), or null when its key or its block has a problem.</summary>
        private ProcedureOverride? Procedure(JsonMember member, string path)
        {
            var at = new JsonLocation(file, path, member.Line, member.Column);
            var procedure = ProcedureKey(member.Name, at);
            if (Object(member.Value, path) is not { } body)
                return null;

            var settings = Settings(body, path);
            CheckTogether(body, path);

            // A declaration with paths is refused above; one that has problems of its own leaves none to add.
            if (procedure is not { } named)
                return null;
            if (settings.Results is null)
                return new InferredResultsOverride(named, member.Name, settings.Name, settings.Paths, at, settings.NameAt) { Sets = settings.Sets, SetsAt = settings.SetsAt };
            return Declared(settings.Results, Child(path, "results"), named.ToString(), settings.Why ?? "") is { } declared
                ? new DeclaredResultsOverride(named, member.Name, settings.Name, declared, at, settings.NameAt) { Sets = settings.Sets, SetsAt = settings.SetsAt }
                : null;
        }

        /// <summary>The procedure a key of <c>procedures</c> names: one procedure, so a pattern without wildcards.</summary>
        private ObjectName? ProcedureKey(string key, JsonLocation at)
        {
            if (!ObjectPattern.TryParse(key, out var pattern, out var problem))
            {
                Problems.Add(ConfigCodes.BadPattern.At(at, problem));
                return null;
            }

            if (Wildcard(key) || pattern.Name is not { } literal)
            {
                Problems.Add(ConfigCodes.WildcardProcedure.At(at, "names one procedure, so it cannot use * or ?; write a part that holds them in brackets, as [a*b]."));
                return null;
            }

            return literal;
        }

        /// <summary>A procedure's block, each setting read where it is written.</summary>
        private ProcedureSettings Settings(JsonObjectValue body, string path)
        {
            var settings = new ProcedureSettings(null, null, [], null, null);
            foreach (var setting in body.Members)
            {
                var settingPath = Child(path, setting.Name);
                switch (setting.Name)
                {
                    case "results":
                        settings = settings with { Results = setting };
                        break;
                    case "why":
                        settings = settings with { Why = NonEmpty(setting.Value, settingPath) };
                        break;
                    case "name":
                        settings = settings with { Name = Identifier(setting.Value, settingPath), NameAt = At(setting.Value, settingPath) };
                        break;
                    case "paths":
                        settings = settings with { Paths = [.. PathOverrides(setting.Value, settingPath)] };
                        break;
                    case "sets":
                        settings = settings with { Sets = SetNames(setting.Value, settingPath), SetsAt = At(setting.Value, settingPath) };
                        break;
                    default:
                        Unknown(setting, settingPath, "name, paths, results, sets, why");
                        break;
                }
            }

            return settings;
        }

        /// <summary>
        /// <c>sets</c>: a name for each of the procedure's result sets, in order (<see cref="SetNameRules"/>); or null when one
        /// can't be used, which is reported.
        /// </summary>
        private EquatableArray<string>? SetNames(JsonValue value, string path)
        {
            if (value is not JsonArrayValue { Items.Count: > 0 } array)
            {
                Report(ConfigCodes.BadSetNames, value, path, "must be an array of names, one for each result set in order, such as [\"order\", \"lines\"].");
                return null;
            }

            var names = new List<string>();
            var errors = Problems.Count(p => p.IsError);
            for (var i = 0; i < array.Items.Count; i++)
            {
                var item = array.Items[i];
                if (String(item, Index(path, i)) is not { } name)
                    continue;
                if (SetNameRules.Problem(name, names, reserved) is { } problem)
                    Report(ConfigCodes.BadSetNames, item, Index(path, i), $"'{name}' {problem}");
                names.Add(name);
            }

            return Problems.Count(p => p.IsError) > errors ? null : [.. names];
        }

        private List<PathOverride> PathOverrides(JsonValue value, string path)
        {
            var paths = new List<PathOverride>();
            if (Object(value, path) is { } overrides)
            {
                foreach (var entry in overrides.Members)
                {
                    if (PathOverride(entry, Child(path, entry.Name)) is { } parsed)
                        paths.Add(parsed);
                }
            }

            return paths;
        }

        /// <summary>The settings of a procedure's block that must come together, or never.</summary>
        private void CheckTogether(JsonObjectValue body, string path)
        {
            var has = body.Members.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
            if (has.Contains("results") && !has.Contains("why"))
                Report(ConfigCodes.ClaimWithoutWhy, body, path, "\"results\" needs \"why\": the project doesn't show them, so say what does.");
            if (has.Contains("why") && !has.Contains("results"))
                Report(ConfigCodes.WhyWithoutClaim, body, path, "\"why\" explains declared results, but there are none here: give \"results\", or move \"why\" into the path it explains.");
            if (has.Contains("results") && has.Contains("paths"))
                Report(ConfigCodes.PathsWithResults, body, path, "\"paths\" can't be used with \"results\": the declared schema says it all, so put the detail there.");
        }

        /// <summary>
        /// <c>results</c>: <c>"none"</c>, one result (<c>{ "rowset": schema }</c> or <c>{ "json": schema, "mayBeAbsent": … }</c>),
        /// or an array of them; a schema is an object, or the path of a file holding one, relative to this file.
        /// </summary>
        private ResultsDeclaration? Declared(JsonMember member, string path, string procedure, string why)
        {
            var at = new JsonLocation(file, path, member.Line, member.Column);
            var errors = Problems.Count(p => p.IsError);
            var types = new Dictionary<string, DeclaredType>(StringComparer.Ordinal);
            var results = new List<IrResult>();
            switch (member.Value)
            {
                case JsonStringValue { Value: "none" }:
                    break;
                case JsonObjectValue one:
                    if (Result(one, path, 0, procedure, types) is { } single)
                        results.Add(single);
                    break;
                case JsonArrayValue { Items.Count: 0 } empty:
                    Report(ConfigCodes.WrongKind, empty, path, "must list at least one result; for none, use \"none\".");
                    break;
                case JsonArrayValue many:
                    for (var i = 0; i < many.Items.Count; i++)
                    {
                        if (Object(many.Items[i], Index(path, i)) is { } item && Result(item, Index(path, i), i, procedure, types) is { } result)
                            results.Add(result);
                    }

                    break;
                default:
                    Report(ConfigCodes.WrongKind, member.Value, path, "must be \"none\", a result ({ \"rowset\": … } or { \"json\": … }) or an array of them, in the order the procedure returns them.");
                    break;
            }

            return Problems.Count(p => p.IsError) == errors ? new ResultsDeclaration([.. results], [.. types], why, at) : null;
        }

        private IrResult? Result(JsonObjectValue result, string path, int index, string procedure, Dictionary<string, DeclaredType> types)
        {
            JsonMember? schema = null;
            var json = false;
            var mayBeAbsent = false;
            foreach (var member in result.Members)
            {
                var memberPath = Child(path, member.Name);
                switch (member.Name)
                {
                    case "rowset" or "json":
                        if (schema is not null)
                        {
                            Report(ConfigCodes.RowsetOrJson, result, path, "must have either \"rowset\" or \"json\", the schema of a row or of the JSON document, not both.");
                            return null;
                        }

                        schema = member;
                        json = member.Name == "json";
                        break;
                    case "mayBeAbsent":
                        if (member.Value is JsonOtherValue { Kind: JsonTokenType.True or JsonTokenType.False } flag)
                            mayBeAbsent = flag.Kind == JsonTokenType.True;
                        else
                            Report(ConfigCodes.WrongKind, member.Value, memberPath, "must be true or false.");
                        break;
                    default:
                        Unknown(member, memberPath, "rowset, json, mayBeAbsent");
                        break;
                }
            }

            if (schema is null)
            {
                Report(ConfigCodes.RowsetOrJson, result, path, "must have either \"rowset\" or \"json\", the schema of a row or of the JSON document.");
                return null;
            }

            if (!json && result.Members.Any(m => m.Name == "mayBeAbsent"))
            {
                Report(ConfigCodes.AbsentRowset, result, path, "\"mayBeAbsent\" is for a JSON result, which FOR JSON leaves out when it matches no rows; a rowset with no rows is still there.");
                return null;
            }

            if (SchemaRoot(schema, Child(path, schema.Name)) is not var (schemaFile, root, rootPath))
                return null;

            var translator = new DeclaredSchema(schemaFile, root, rootPath, Problems, procedure, types);
            return json ? translator.Json(index, mayBeAbsent) : translator.Rowset(index);
        }

        /// <summary>
        /// Where a declared result's schema is: written in place, or in a file of its own, relative to this one like every path
        /// here, whose problems are reported in it. Null when it can't be read.
        /// </summary>
        private (string File, JsonValue Root, string RootPath)? SchemaRoot(JsonMember schema, string schemaPath)
        {
            switch (schema.Value)
            {
                case JsonObjectValue:
                    return (file, schema.Value, schemaPath);
                case JsonStringValue { Value.Length: > 0 } reference:
                    var schemaFile = Path.GetFullPath(Path.Combine(directory, reference.Value));
                    byte[] bytes;
                    try
                    {
                        bytes = File.ReadAllBytes(schemaFile);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        Report(ConfigCodes.Unreadable, reference, schemaPath, $"cannot read the schema {reference.Value}: {e.Message}");
                        return null;
                    }

                    var parsed = ConfigJson.Parse(bytes, schemaFile);
                    Problems.AddRange(parsed.Diagnostics);
                    return parsed is Succeeded<JsonValue> { Value: var read } ? (schemaFile, read, "") : null;
                default:
                    Report(ConfigCodes.WrongKind, schema.Value, schemaPath, "must be a JSON Schema, or the path of a file holding one, relative to this file.");
                    return null;
            }
        }

        private PathOverride? PathOverride(JsonMember entry, string path)
        {
            var at = new JsonLocation(file, path, entry.Line, entry.Column);
            if (!IrPath.TryParse(entry.Name, out _, out var problem))
                Problems.Add(ConfigCodes.BadPath.At(at, problem));
            if (Object(entry.Value, path) is not { } body)
                return null;

            var result = new PathOverride(entry.Name, at);
            var errors = Problems.Count;
            string? importedName = null;
            string? importedFrom = null;
            foreach (var member in body.Members)
            {
                var memberPath = Child(path, member.Name);
                switch (member.Name)
                {
                    case "name":
                        result = result with { Name = Identifier(member.Value, memberPath) };
                        break;
                    case "nullable":
                        if (member.Value is JsonOtherValue { Kind: JsonTokenType.False })
                            result = result with { NotNull = true };
                        else if (member.Value is JsonOtherValue { Kind: JsonTokenType.True })
                            Report(ConfigCodes.NullableTrue, member.Value, memberPath, "can only be false. A value is nullable unless the database proves otherwise, so there is nothing to declare.");
                        else
                            Report(ConfigCodes.WrongKind, member.Value, memberPath, "must be false.");
                        break;
                    case "why":
                        result = result with { Why = NonEmpty(member.Value, memberPath) };
                        break;
                    case "type":
                        if (String(member.Value, memberPath) is { } type)
                        {
                            if (Regex.IsMatch(type, @"^[A-Za-z_$][A-Za-z0-9_$]*(\[\])*$", RegexOptions.CultureInvariant)
                                && !reserved.Contains(type.TrimEnd('[', ']')))
                            {
                                importedName = type;
                            }
                            else
                            {
                                Report(ConfigCodes.BadName, member.Value, memberPath, "must be the name of an exported schema, with [] for each level of array, such as \"TagSchema[]\".");
                            }
                        }

                        break;
                    case "import":
                        importedFrom = NonEmpty(member.Value, memberPath);
                        break;
                    case "values":
                        if (Choice(member.Value, memberPath, "exact") is not null)
                            result = result with { ExactValues = true };
                        break;
                    default:
                        Unknown(member, memberPath, "name, nullable, why, type, import, values");
                        break;
                }
            }

            if (importedName is not null && importedFrom is not null)
                result = result with { Imported = new ImportedType(importedName, importedFrom) };

            var has = body.Members.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
            if (!has.Overlaps(["name", "nullable", "type", "values"]) && has.IsSubsetOf(["why", "import"]))
                Report(ConfigCodes.EmptyOverride, body, path, "says nothing to override: give \"name\", \"nullable\", \"type\" or \"values\".");
            if (has.Contains("nullable") && !has.Contains("why"))
                Report(ConfigCodes.ClaimWithoutWhy, body, path, "\"nullable\": false needs \"why\": the database doesn't prove it, so say what does.");
            if (has.Contains("values") && !has.Contains("why"))
                Report(ConfigCodes.ClaimWithoutWhy, body, path, "\"values\": \"exact\" needs \"why\": the database compares the values under its collation, so say why they are exact.");
            if (has.Contains("type") && !has.Contains("import"))
                Report(ConfigCodes.NeedsAnother, body, path, "\"type\" needs \"import\": the module that exports the schema.");
            if (has.Contains("import") && !has.Contains("type"))
                Report(ConfigCodes.NeedsAnother, body, path, "\"import\" needs \"type\": the schema to import from it.");
            if (has.Contains("why") && !has.Overlaps(["nullable", "type", "values"]))
                Report(ConfigCodes.WhyWithoutClaim, body, path, "\"why\" explains a claim, but there is none here: give \"nullable\", \"type\" or \"values\".");
            return Problems.Count == errors ? result : null;
        }

        private static bool Wildcard(string pattern)
        {
            var inBrackets = false;
            for (var i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] == '[' && !inBrackets)
                    inBrackets = true;
                else if (pattern[i] == ']' && inBrackets && (i + 1 >= pattern.Length || pattern[i + 1] != ']'))
                    inBrackets = false;
                else if (pattern[i] == ']' && inBrackets)
                    i++;
                else if (!inBrackets && pattern[i] is '*' or '?')
                    return true;
            }

            return false;
        }

        private string? Identifier(JsonValue value, string path)
        {
            if (String(value, path) is not { } name)
                return null;
            if (IrIdentifiers.IsIdentifier(name) && !reserved.Contains(name))
                return name;
            Report(ConfigCodes.BadName, value, path, $"'{name}' is not a valid name: an ASCII letter, _ or $, then letters, digits, _ or $, and not a reserved word.");
            return null;
        }

        private JsonObjectValue? Object(JsonValue value, string path)
        {
            if (value is JsonObjectValue obj)
                return obj;
            Report(ConfigCodes.WrongKind, value, path, "must be an object.");
            return null;
        }

        private string? String(JsonValue value, string path)
        {
            if (value is JsonStringValue text)
                return text.Value;
            Report(ConfigCodes.WrongKind, value, path, "must be a string.");
            return null;
        }

        private string? NonEmpty(JsonValue value, string path)
        {
            var text = String(value, path);
            if (text is { Length: 0 })
            {
                Report(ConfigCodes.WrongKind, value, path, "must not be empty.");
                return null;
            }

            return text;
        }

        private string? Choice(JsonValue value, string path, params string[] choices)
        {
            var text = String(value, path);
            if (text is null || choices.Contains(text, StringComparer.Ordinal))
                return text;
            Report(ConfigCodes.WrongKind, value, path, $"must be {string.Join(" or ", choices.Select(c => $"\"{c}\""))}, not \"{text}\".");
            return null;
        }

        private void Unknown(JsonMember member, string path, string known) =>
            Problems.Add(ConfigCodes.UnknownSetting.At(new JsonLocation(file, path, member.Line, member.Column), $"unknown setting \"{member.Name}\". The settings here are {known}."));

        private void Report(DiagnosticCode code, JsonValue value, string path, string message) =>
            Problems.Add(code.At(At(value, path), message));

        private JsonLocation At(JsonValue value, string path) => new(file, path, value.Line, value.Column);

        private static string Child(string path, string key) => ConfigPath.Child(path, key);

        private static string Index(string path, int index) => ConfigPath.Index(path, index);
    }
}
