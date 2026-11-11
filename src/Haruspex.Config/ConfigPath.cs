using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Haruspex.Ir;

namespace Haruspex.Config;

/// <summary>How a place in the config file is written in problems: <c>schemas.sales.include[0]</c>, <c>procedures["sales.get_order"]</c>.</summary>
internal static class ConfigPath
{
    public static string Child(string path, string key)
    {
        var segment = IrIdentifiers.IsIdentifier(key) ? key : "[\"" + JsonEncodedText.Encode(key, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"]";
        return path.Length == 0 || segment.StartsWith('[') ? path + segment : path + "." + segment;
    }

    public static string Index(string path, int index) => path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
}
