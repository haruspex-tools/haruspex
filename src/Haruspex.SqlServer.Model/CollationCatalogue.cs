using System.Collections.Frozen;
using Haruspex.Collections;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// Every collation SQL Server has, by name, with the code page its <c>char</c> and <c>varchar</c> data is stored in: a closed list,
/// so that a name nothing can place (a misspelt <c>_UTF8</c>, a designator SQL Server lacks) is known to be unknown, never read as
/// some other collation. It is checked against <c>sys.fn_helpcollations()</c> both ways (observed:
/// The_collations_haruspex_lists_are_exactly_the_servers_with_their_code_pages).
/// </summary>
/// <remarks>
/// <para>
/// A Windows collation's name is a designator (<c>Latin1_General</c>), an optional version (<c>_90</c>, <c>_100</c>,
/// <c>_140</c>, <c>_160</c>), and options. Each designator has one code page, and the options a version has are the same for
/// every designator of that version, so the list is the designators with their versions, and the options are generated:
/// <c>_BIN</c> or <c>_BIN2</c>, or case (<c>_CI</c>, <c>_CS</c>), accents (<c>_AI</c>, <c>_AS</c>), then optionally kana
/// (<c>_KS</c>), width (<c>_WS</c>) and, for 140, variation selectors (<c>_VSS</c>); versions 90 and 100 add supplementary
/// characters (<c>_SC</c>, then <c>_UTF8</c> after it), 140 adds <c>_UTF8</c> alone, and 160 has only <c>_SC_UTF8</c>
/// collations. One name falls outside the pattern: <c>Latin1_General_100_BIN2_UTF8</c>. A <c>SQL_</c> collation's name follows no
/// pattern, so each is listed.
/// </para>
/// <para>
/// Names compare ignoring case, as SQL Server compares them. The list is SQL Server 2025's; a collation a later version adds is
/// unknown until it is added here.
/// </para>
/// </remarks>
public static class CollationCatalogue
{
    /// <summary>The code page of a <c>_UTF8</c> collation's <c>char</c> and <c>varchar</c> data.</summary>
    public const int Utf8 = 65001;

    /// <summary>The code page of a Unicode-only collation, which <c>char</c> and <c>varchar</c> can't use.</summary>
    public const int UnicodeOnly = 0;

    /// <summary>Every collation's name, as SQL Server spells it.</summary>
    public static IEnumerable<string> Names => CodePages.Keys;

    /// <summary>Whether <paramref name="name"/> is a collation SQL Server has, ignoring case.</summary>
    /// <param name="name">The collation's name.</param>
    public static bool IsKnown(string name) => CodePages.ContainsKey(name ?? throw new ArgumentNullException(nameof(name)));

    /// <summary>
    /// The code page <c>char</c> and <c>varchar</c> data is stored in under the collation, as <c>COLLATIONPROPERTY(name,
    /// 'CodePage')</c> gives it: <see cref="Utf8"/> for a <c>_UTF8</c> one, <see cref="UnicodeOnly"/> for one only Unicode data
    /// may use; null for a name that is no collation's.
    /// </summary>
    /// <param name="name">The collation's name.</param>
    public static int? CodePage(string name) =>
        CodePages.TryGetValue(name ?? throw new ArgumentNullException(nameof(name)), out var codePage) ? codePage : null;

    private static FrozenDictionary<string, int> Build()
    {
        var all = new Dictionary<string, int>(SqlCollations, StringComparer.OrdinalIgnoreCase);
        foreach (var designator in Designators)
        {
            foreach (var version in designator.Versions)
            {
                var stem = version.Length == 0 ? designator.Name : $"{designator.Name}_{version}";
                foreach (var options in Options(version))
                    all.Add($"{stem}_{options}", options.EndsWith("UTF8", StringComparison.Ordinal) ? Utf8 : designator.CodePage);
            }
        }

        all.Add("Latin1_General_100_BIN2_UTF8", Utf8);
        return all.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The options every designator of a version has (see the remarks).</summary>
    private static IEnumerable<string> Options(string version)
    {
        var sensitivities =
            from c in Cases
            from a in Accents
            from k in Kana
            from w in Width
            select $"{c}_{a}{k}{w}";
        string[] suffixes = version switch
        {
            "" => [""],
            "90" or "100" => ["", "_SC", "_SC_UTF8"],
            "140" => ["", "_UTF8", "_VSS", "_VSS_UTF8"],
            "160" => ["_SC_UTF8"],
            _ => throw new ArgumentException($"No collation has the version {version}.", nameof(version)),
        };
        return (version == "160" ? [] : Binary).Concat(from s in sensitivities from suffix in suffixes select s + suffix);
    }

    private static readonly string[] Binary = ["BIN", "BIN2"];
    private static readonly string[] Cases = ["CI", "CS"];
    private static readonly string[] Accents = ["AI", "AS"];
    private static readonly string[] Kana = ["", "_KS"];
    private static readonly string[] Width = ["", "_WS"];

    /// <summary>A designator, the code page of its non-UTF-8 collations, and the versions it has ("" for none).</summary>
    private sealed record Designator(string Name, int CodePage, EquatableArray<string> Versions);

    private static readonly Designator[] Designators =
    [
        new("Albanian", 1250, ["", "100"]),
        new("Arabic", 1256, ["", "100"]),
        new("Assamese", 0, ["100"]),
        new("Azeri_Cyrillic", 1251, ["100"]),
        new("Azeri_Latin", 1254, ["100"]),
        new("Bashkir", 1251, ["100"]),
        new("Bengali", 0, ["100"]),
        new("Bosnian_Cyrillic", 1251, ["100"]),
        new("Bosnian_Latin", 1250, ["100"]),
        new("Breton", 1252, ["100"]),
        new("Chinese_Hong_Kong_Stroke", 950, ["90"]),
        new("Chinese_PRC", 936, ["", "90"]),
        new("Chinese_PRC_Stroke", 936, ["", "90"]),
        new("Chinese_Simplified_Pinyin", 936, ["100", "160"]),
        new("Chinese_Simplified_Stroke_Order", 936, ["100", "160"]),
        new("Chinese_Taiwan_Bopomofo", 950, ["", "90"]),
        new("Chinese_Taiwan_Stroke", 950, ["", "90"]),
        new("Chinese_Traditional_Bopomofo", 950, ["100"]),
        new("Chinese_Traditional_Pinyin", 950, ["100"]),
        new("Chinese_Traditional_Stroke_Count", 950, ["100"]),
        new("Chinese_Traditional_Stroke_Order", 950, ["100"]),
        new("Corsican", 1252, ["100"]),
        new("Croatian", 1250, ["", "100"]),
        new("Cyrillic_General", 1251, ["", "100"]),
        new("Czech", 1250, ["", "100"]),
        new("Danish_Greenlandic", 1252, ["100"]),
        new("Danish_Norwegian", 1252, [""]),
        new("Dari", 1256, ["100"]),
        new("Divehi", 0, ["90", "100"]),
        new("Estonian", 1257, ["", "100"]),
        new("Finnish_Swedish", 1252, ["", "100"]),
        new("French", 1252, ["", "100"]),
        new("Frisian", 1252, ["100"]),
        new("Georgian_Modern_Sort", 1252, ["", "100"]),
        new("German_PhoneBook", 1252, ["", "100"]),
        new("Greek", 1253, ["", "100"]),
        new("Hebrew", 1255, ["", "100"]),
        new("Hungarian", 1250, ["", "100"]),
        new("Hungarian_Technical", 1250, ["", "100"]),
        new("Icelandic", 1252, ["", "100"]),
        new("Indic_General", 0, ["90", "100"]),
        new("Japanese", 932, ["", "90"]),
        new("Japanese_Bushu_Kakusu", 932, ["100", "140"]),
        new("Japanese_Unicode", 932, [""]),
        new("Japanese_XJIS", 932, ["100", "140"]),
        new("Kazakh", 1251, ["90", "100"]),
        new("Khmer", 0, ["100"]),
        new("Korean", 949, ["90", "100"]),
        new("Korean_Wansung", 949, [""]),
        new("Lao", 0, ["100"]),
        new("Latin1_General", 1252, ["", "100"]),
        new("Latvian", 1257, ["", "100"]),
        new("Lithuanian", 1257, ["", "100"]),
        new("Macedonian_FYROM", 1251, ["90", "100"]),
        new("Maltese", 0, ["100"]),
        new("Maori", 0, ["100"]),
        new("Mapudungan", 1252, ["100"]),
        new("Modern_Spanish", 1252, ["", "100"]),
        new("Mohawk", 1252, ["100"]),
        new("Nepali", 0, ["100"]),
        new("Norwegian", 1252, ["100"]),
        new("Pashto", 0, ["100"]),
        new("Persian", 1256, ["100"]),
        new("Polish", 1250, ["", "100"]),
        new("Romanian", 1250, ["", "100"]),
        new("Romansh", 1252, ["100"]),
        new("Sami_Norway", 1252, ["100"]),
        new("Sami_Sweden_Finland", 1252, ["100"]),
        new("Serbian_Cyrillic", 1251, ["100"]),
        new("Serbian_Latin", 1250, ["100"]),
        new("Slovak", 1250, ["", "100"]),
        new("Slovenian", 1250, ["", "100"]),
        new("Syriac", 0, ["90", "100"]),
        new("Tamazight", 1252, ["100"]),
        new("Tatar", 1251, ["90", "100"]),
        new("Thai", 874, ["", "100"]),
        new("Tibetan", 0, ["100"]),
        new("Traditional_Spanish", 1252, ["", "100"]),
        new("Turkish", 1254, ["", "100"]),
        new("Turkmen", 1250, ["100"]),
        new("Uighur", 1256, ["100"]),
        new("Ukrainian", 1251, ["", "100"]),
        new("Upper_Sorbian", 1252, ["100"]),
        new("Urdu", 1256, ["100"]),
        new("Uzbek_Latin", 1254, ["90", "100"]),
        new("Vietnamese", 1258, ["", "100"]),
        new("Welsh", 1252, ["100"]),
        new("Yakut", 1251, ["100"]),
    ];

    private static readonly Dictionary<string, int> SqlCollations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SQL_1xCompat_CP850_CI_AS"] = 850,
        ["SQL_AltDiction2_CP1253_CS_AS"] = 1253,
        ["SQL_AltDiction_CP850_CI_AI"] = 850,
        ["SQL_AltDiction_CP850_CI_AS"] = 850,
        ["SQL_AltDiction_CP850_CS_AS"] = 850,
        ["SQL_AltDiction_Pref_CP850_CI_AS"] = 850,
        ["SQL_Croatian_CP1250_CI_AS"] = 1250,
        ["SQL_Croatian_CP1250_CS_AS"] = 1250,
        ["SQL_Czech_CP1250_CI_AS"] = 1250,
        ["SQL_Czech_CP1250_CS_AS"] = 1250,
        ["SQL_Danish_Pref_CP1_CI_AS"] = 1252,
        ["SQL_EBCDIC037_CP1_CS_AS"] = 1252,
        ["SQL_EBCDIC1141_CP1_CS_AS"] = 1252,
        ["SQL_EBCDIC273_CP1_CS_AS"] = 1252,
        ["SQL_EBCDIC277_2_CP1_CS_AS"] = 1252,
        ["SQL_EBCDIC277_CP1_CS_AS"] = 1252,
        ["SQL_EBCDIC278_CP1_CS_AS"] = 1252,
        ["SQL_EBCDIC280_CP1_CS_AS"] = 1252,
        ["SQL_EBCDIC284_CP1_CS_AS"] = 1252,
        ["SQL_EBCDIC285_CP1_CS_AS"] = 1252,
        ["SQL_EBCDIC297_CP1_CS_AS"] = 1252,
        ["SQL_Estonian_CP1257_CI_AS"] = 1257,
        ["SQL_Estonian_CP1257_CS_AS"] = 1257,
        ["SQL_Hungarian_CP1250_CI_AS"] = 1250,
        ["SQL_Hungarian_CP1250_CS_AS"] = 1250,
        ["SQL_Icelandic_Pref_CP1_CI_AS"] = 1252,
        ["SQL_Latin1_General_CP1250_CI_AS"] = 1250,
        ["SQL_Latin1_General_CP1250_CS_AS"] = 1250,
        ["SQL_Latin1_General_CP1251_CI_AS"] = 1251,
        ["SQL_Latin1_General_CP1251_CS_AS"] = 1251,
        ["SQL_Latin1_General_CP1253_CI_AI"] = 1253,
        ["SQL_Latin1_General_CP1253_CI_AS"] = 1253,
        ["SQL_Latin1_General_CP1253_CS_AS"] = 1253,
        ["SQL_Latin1_General_CP1254_CI_AS"] = 1254,
        ["SQL_Latin1_General_CP1254_CS_AS"] = 1254,
        ["SQL_Latin1_General_CP1255_CI_AS"] = 1255,
        ["SQL_Latin1_General_CP1255_CS_AS"] = 1255,
        ["SQL_Latin1_General_CP1256_CI_AS"] = 1256,
        ["SQL_Latin1_General_CP1256_CS_AS"] = 1256,
        ["SQL_Latin1_General_CP1257_CI_AS"] = 1257,
        ["SQL_Latin1_General_CP1257_CS_AS"] = 1257,
        ["SQL_Latin1_General_CP1_CI_AI"] = 1252,
        ["SQL_Latin1_General_CP1_CI_AS"] = 1252,
        ["SQL_Latin1_General_CP1_CS_AS"] = 1252,
        ["SQL_Latin1_General_CP437_BIN"] = 437,
        ["SQL_Latin1_General_CP437_BIN2"] = 437,
        ["SQL_Latin1_General_CP437_CI_AI"] = 437,
        ["SQL_Latin1_General_CP437_CI_AS"] = 437,
        ["SQL_Latin1_General_CP437_CS_AS"] = 437,
        ["SQL_Latin1_General_CP850_BIN"] = 850,
        ["SQL_Latin1_General_CP850_BIN2"] = 850,
        ["SQL_Latin1_General_CP850_CI_AI"] = 850,
        ["SQL_Latin1_General_CP850_CI_AS"] = 850,
        ["SQL_Latin1_General_CP850_CS_AS"] = 850,
        ["SQL_Latin1_General_Pref_CP1_CI_AS"] = 1252,
        ["SQL_Latin1_General_Pref_CP437_CI_AS"] = 437,
        ["SQL_Latin1_General_Pref_CP850_CI_AS"] = 850,
        ["SQL_Latvian_CP1257_CI_AS"] = 1257,
        ["SQL_Latvian_CP1257_CS_AS"] = 1257,
        ["SQL_Lithuanian_CP1257_CI_AS"] = 1257,
        ["SQL_Lithuanian_CP1257_CS_AS"] = 1257,
        ["SQL_MixDiction_CP1253_CS_AS"] = 1253,
        ["SQL_Polish_CP1250_CI_AS"] = 1250,
        ["SQL_Polish_CP1250_CS_AS"] = 1250,
        ["SQL_Romanian_CP1250_CI_AS"] = 1250,
        ["SQL_Romanian_CP1250_CS_AS"] = 1250,
        ["SQL_Scandinavian_CP850_CI_AS"] = 850,
        ["SQL_Scandinavian_CP850_CS_AS"] = 850,
        ["SQL_Scandinavian_Pref_CP850_CI_AS"] = 850,
        ["SQL_Slovak_CP1250_CI_AS"] = 1250,
        ["SQL_Slovak_CP1250_CS_AS"] = 1250,
        ["SQL_Slovenian_CP1250_CI_AS"] = 1250,
        ["SQL_Slovenian_CP1250_CS_AS"] = 1250,
        ["SQL_SwedishPhone_Pref_CP1_CI_AS"] = 1252,
        ["SQL_SwedishStd_Pref_CP1_CI_AS"] = 1252,
        ["SQL_Ukrainian_CP1251_CI_AS"] = 1251,
        ["SQL_Ukrainian_CP1251_CS_AS"] = 1251,
    };

    // Last: a static field is initialised in the order written, and this one reads the two above.
    private static readonly FrozenDictionary<string, int> CodePages = Build();
}
