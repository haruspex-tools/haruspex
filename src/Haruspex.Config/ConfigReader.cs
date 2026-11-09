using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Haruspex.Ir;

namespace Haruspex.Config;

/// <summary>The outcome of reading a config file: the config when it has no errors, and every problem found.</summary>
public sealed record ConfigReadResult(HaruspexConfig? Config, IReadOnlyList<ConfigProblem> Problems);

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
    public static ConfigReadResult Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(null, [new ConfigProblem(ConfigSeverity.Error, path, null, $"cannot read the config file: {e.Message}")]);
        }

        return Read(bytes, path, Path.GetDirectoryName(Path.GetFullPath(path))!);
    }

    /// <summary>Reads and checks config text.</summary>
    /// <param name="utf8">The file's bytes.</param>
    /// <param name="file">The file's name, for problems.</param>
    /// <param name="directory">The folder its relative paths are relative to.</param>
    public static ConfigReadResult Read(byte[] utf8, string file, string directory)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(directory);
        JsonValue root;
        try
        {
            root = ConfigJson.Parse(utf8);
        }
        catch (ConfigJsonException e)
        {
            return new(null, [new ConfigProblem(ConfigSeverity.Error, file, new ConfigLocation(e.Path, e.Line, e.Column), e.Message)]);
        }

        var reader = new Checker(file);
        var config = reader.Root(root, directory);
        return new(reader.Problems.Count == 0 ? config : null, reader.Problems);
    }

    private sealed class Checker(string file)
    {
        public List<ConfigProblem> Problems { get; } = [];

        public HaruspexConfig Root(JsonValue value, string directory)
        {
            var config = new HaruspexConfig { File = file, Directory = directory };
            if (Object(value, "") is not { } root)
                return config;

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
                    case "out":
                        if (NonEmpty(member.Value, path) is { } output)
                            config = config with { Out = Path.GetFullPath(Path.Combine(directory, output)) };
                        break;
                    case "schemas":
                        config = config with { Schemas = Schemas(member.Value, path) };
                        break;
                    case "objects":
                        if (Choice(member.Value, path, "strip", "strict") is { } objects)
                            config = config with { Objects = objects == "strict" ? ObjectsPolicy.Strict : ObjectsPolicy.Strip };
                        break;
                    case "naming":
                        var naming = Naming(member.Value, path);
                        config = config with { Naming = new NamingRules(naming.Replace, naming.Case ?? NamingCase.Pascal), NamingAt = naming.At };
                        break;
                    case "procedures":
                        config = config with { Procedures = Procedures(member.Value, path) };
                        break;
                    default:
                        Unknown(member, path, "$schema, source, out, schemas, objects, naming, procedures");
                        break;
                }
            }

            return config;
        }

        private List<SchemaSettings> Schemas(JsonValue value, string path)
        {
            var schemas = new List<SchemaSettings>();
            if (Object(value, path) is not { } members)
                return schemas;

            foreach (var member in members.Members)
            {
                var schemaPath = Child(path, member.Name);
                var at = new ConfigLocation(schemaPath, member.Line, member.Column);
                if (member.Name.Length == 0)
                {
                    Problems.Add(new ConfigProblem(ConfigSeverity.Error, file, at, "a schema's name must not be empty."));
                    continue;
                }

                if (schemas.FirstOrDefault(s => string.Equals(s.Name, member.Name, StringComparison.OrdinalIgnoreCase)) is { } earlier)
                {
                    Problems.Add(new ConfigProblem(ConfigSeverity.Error, file, at,
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
                            schema = schema with { Include = Patterns(setting.Value, settingPath, requireOne: true) };
                            break;
                        case "exclude":
                            exclude = setting;
                            schema = schema with { Exclude = Patterns(setting.Value, settingPath, requireOne: false) };
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
                    Problems.Add(new ConfigProblem(ConfigSeverity.Error, file, new ConfigLocation(Child(schemaPath, "exclude"), exclude.Line, exclude.Column),
                        "does nothing without an \"include\" in the same block, since nothing in the schema is selected. Add the include, or remove the exclude."));
                }

                schemas.Add(schema);
            }

            return schemas;
        }

        private List<NamePatternEntry> Patterns(JsonValue value, string path, bool requireOne)
        {
            var patterns = new List<NamePatternEntry>();
            if (value is not JsonArrayValue array)
            {
                Error(value, path, "must be an array of patterns matching the schema's procedures by name, such as \"get_*\".");
                return patterns;
            }

            if (requireOne && array.Items.Count == 0)
                Error(value, path, "must list at least one pattern; with none, nothing is selected. Use \"*\" to select every procedure of the schema.");

            for (var i = 0; i < array.Items.Count; i++)
            {
                var itemPath = Index(path, i);
                if (String(array.Items[i], itemPath) is not { } text)
                    continue;
                if (NamePattern.TryParse(text, out var pattern, out var problem))
                    patterns.Add(new NamePatternEntry(pattern, At(array.Items[i], itemPath)));
                else
                    Error(array.Items[i], itemPath, problem);
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
                        naming = naming with { Replace = Replacements(member.Value, memberPath) };
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
                Error(value, path, "must be an array of { \"pattern\": \"…\", \"with\": \"…\" }.");
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
                                    Error(member.Value, memberPath, $"is not a regular expression haruspex can use: {e.Message}");
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
                    Error(rule, itemPath, "needs \"pattern\", the regular expression to replace.");
                if (!rule.Members.Any(m => m.Name == "with"))
                    Error(rule, itemPath, "needs \"with\", the replacement (\"\" to remove the match).");
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
                var procedurePath = Child(path, member.Name);
                var at = new ConfigLocation(procedurePath, member.Line, member.Column);
                ObjectPattern? procedure = null;
                if (!ObjectPattern.TryParse(member.Name, out var pattern, out var problem))
                    Problems.Add(new ConfigProblem(ConfigSeverity.Error, file, at, problem));
                else if (Wildcard(member.Name))
                    Problems.Add(new ConfigProblem(ConfigSeverity.Error, file, at, "names one procedure, so it cannot use * or ?; write a part that holds them in brackets, as [a*b]."));
                else
                    procedure = pattern;

                if (Object(member.Value, procedurePath) is not { } body)
                    continue;

                string? name = null;
                ConfigLocation? nameAt = null;
                var paths = new List<PathOverride>();
                foreach (var setting in body.Members)
                {
                    var settingPath = Child(procedurePath, setting.Name);
                    switch (setting.Name)
                    {
                        case "name":
                            name = Identifier(setting.Value, settingPath);
                            nameAt = At(setting.Value, settingPath);
                            break;
                        case "paths":
                            if (Object(setting.Value, settingPath) is { } overrides)
                            {
                                foreach (var entry in overrides.Members)
                                {
                                    if (PathOverride(entry, Child(settingPath, entry.Name)) is { } parsed)
                                        paths.Add(parsed);
                                }
                            }

                            break;
                        default:
                            Unknown(setting, settingPath, "name, paths");
                            break;
                    }
                }

                if (procedure is not null)
                    procedures.Add(new ProcedureOverride(procedure, name, paths, at, nameAt));
            }

            return procedures;
        }

        private PathOverride? PathOverride(JsonMember entry, string path)
        {
            var at = new ConfigLocation(path, entry.Line, entry.Column);
            if (!IrPath.TryParse(entry.Name, out _, out var problem))
                Problems.Add(new ConfigProblem(ConfigSeverity.Error, file, at, problem!));
            if (Object(entry.Value, path) is not { } body)
                return null;

            var result = new PathOverride(entry.Name, at);
            var errors = Problems.Count;
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
                            Error(member.Value, memberPath, "can only be false. A value is nullable unless the database proves otherwise, so there is nothing to declare.");
                        else
                            Error(member.Value, memberPath, "must be false.");
                        break;
                    case "why":
                        result = result with { Why = NonEmpty(member.Value, memberPath) };
                        break;
                    case "type":
                        if (String(member.Value, memberPath) is { } type)
                        {
                            if (Regex.IsMatch(type, @"^[A-Za-z_$][A-Za-z0-9_$]*(\[\])*$", RegexOptions.CultureInvariant)
                                && !IrIdentifiers.IsReserved(type.TrimEnd('[', ']')))
                                result = result with { Type = type };
                            else
                                Error(member.Value, memberPath, "must be the name of an exported schema, with [] for each level of array, such as \"TagSchema[]\".");
                        }

                        break;
                    case "import":
                        result = result with { Import = NonEmpty(member.Value, memberPath) };
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

            var has = body.Members.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
            if (!has.Overlaps(["name", "nullable", "type", "values"]) && has.IsSubsetOf(["why", "import"]))
                Error(body, path, "says nothing to override: give \"name\", \"nullable\", \"type\" or \"values\".");
            if (has.Contains("nullable") && !has.Contains("why"))
                Error(body, path, "\"nullable\": false needs \"why\": the database doesn't prove it, so say what does.");
            if (has.Contains("values") && !has.Contains("why"))
                Error(body, path, "\"values\": \"exact\" needs \"why\": the database compares the values under its collation, so say why they are exact.");
            if (has.Contains("type") && !has.Contains("import"))
                Error(body, path, "\"type\" needs \"import\": the module that exports the schema.");
            if (has.Contains("import") && !has.Contains("type"))
                Error(body, path, "\"import\" needs \"type\": the schema to import from it.");
            if (has.Contains("why") && !has.Overlaps(["nullable", "type", "values"]))
                Error(body, path, "\"why\" explains a claim, but there is none here: give \"nullable\", \"type\" or \"values\".");
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
            if (IrIdentifiers.IsIdentifier(name) && !IrIdentifiers.IsReserved(name))
                return name;
            Error(value, path, $"'{name}' is not a valid name: an ASCII letter, _ or $, then letters, digits, _ or $, and not a reserved word.");
            return null;
        }

        private JsonObjectValue? Object(JsonValue value, string path)
        {
            if (value is JsonObjectValue obj)
                return obj;
            Error(value, path, "must be an object.");
            return null;
        }

        private string? String(JsonValue value, string path)
        {
            if (value is JsonStringValue text)
                return text.Value;
            Error(value, path, "must be a string.");
            return null;
        }

        private string? NonEmpty(JsonValue value, string path)
        {
            var text = String(value, path);
            if (text is { Length: 0 })
            {
                Error(value, path, "must not be empty.");
                return null;
            }

            return text;
        }

        private string? Choice(JsonValue value, string path, params string[] choices)
        {
            var text = String(value, path);
            if (text is null || choices.Contains(text, StringComparer.Ordinal))
                return text;
            Error(value, path, $"must be {string.Join(" or ", choices.Select(c => $"\"{c}\""))}, not \"{text}\".");
            return null;
        }

        private void Unknown(JsonMember member, string path, string known) =>
            Problems.Add(new ConfigProblem(ConfigSeverity.Error, file, new ConfigLocation(path, member.Line, member.Column), $"unknown setting \"{member.Name}\". The settings here are {known}."));

        private void Error(JsonValue value, string path, string message) =>
            Problems.Add(new ConfigProblem(ConfigSeverity.Error, file, At(value, path), message));

        private static ConfigLocation At(JsonValue value, string path) => new(path, value.Line, value.Column);

        private static string Child(string path, string key) => ConfigPath.Child(path, key);

        private static string Index(string path, int index) => ConfigPath.Index(path, index);
    }
}
