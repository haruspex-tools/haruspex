using System.Globalization;
using System.Text;

namespace Haruspex.Cli.Output;

/// <summary>
/// Text from the input (a name, a path, a message quoting SQL) as the text report prints it: each control character written as
/// an escape (<c>\n</c>, <c>\r</c>, <c>\t</c>, else <c>\x1B</c> and the like), never raw. A bracketed identifier may hold a line
/// break or an escape character; printed raw, a line break would start a new line that a CI runner reads as a command
/// (GitHub Actions takes a line starting <c>::</c> as a workflow command), and an escape character would drive the terminal.
/// The machine formats escape their text as JSON does, and the annotations as the runner's own escaping asks.
/// </summary>
internal static class Visible
{
    /// <summary><paramref name="text"/> with every control character, C0, DEL and C1, written as an escape.</summary>
    public static string Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Any(char.IsControl))
            return text;
        var shown = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            shown.Append(c switch
            {
                '\n' => @"\n",
                '\r' => @"\r",
                '\t' => @"\t",
                _ when char.IsControl(c) => string.Create(CultureInfo.InvariantCulture, $@"\x{(int)c:X2}"),
                _ => c.ToString(),
            });
        }

        return shown.ToString();
    }
}
