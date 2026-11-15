using System.Globalization;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// An MSBuild condition, evaluated as far as decision 0044 goes: <c>==</c> and <c>!=</c> (ignoring case, as MSBuild compares),
/// <c>&lt;</c>, <c>&gt;</c>, <c>&lt;=</c> and <c>&gt;=</c> of numbers, <c>and</c>, <c>or</c>, <c>!</c>, parentheses, the words
/// <c>true</c> and <c>false</c> (and MSBuild's <c>on</c>, <c>off</c>, <c>yes</c>, <c>no</c>), <c>Exists('path')</c> and
/// <c>HasTrailingSlash('path')</c>. Anything else is false with a doubt saying what, so whatever it guards is doubted.
/// </summary>
/// <remarks>
/// <c>Exists</c> takes a path relative to the project's folder, as MSBuild does (not to the file the condition is in), and finds
/// it ignoring case, as <see cref="DiskPath"/> finds every path.
/// </remarks>
internal sealed class ConditionEvaluator
{
    private readonly string text;
    private readonly Func<string, Evaluated> expand;
    private readonly string folder;
    private readonly SourceLocation at;
    private readonly List<Doubt> doubts = [];
    private int position;

    private ConditionEvaluator(string text, Func<string, Evaluated> expand, string folder, SourceLocation at)
    {
        this.text = text;
        this.expand = expand;
        this.folder = folder;
        this.at = at;
    }

    /// <summary>
    /// The condition <paramref name="text"/>, at <paramref name="at"/>, evaluated with <paramref name="expand"/> giving each
    /// quoted or bare value its properties' values; paths in <c>Exists</c> are relative to <paramref name="folder"/>.
    /// </summary>
    public static Truth Evaluate(string text, Func<string, Evaluated> expand, string folder, SourceLocation at)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Truth.Always;
        var evaluator = new ConditionEvaluator(text, expand, folder, at);
        try
        {
            var holds = evaluator.Or();
            evaluator.Space();
            if (evaluator.position < text.Length)
                throw new FormatException($"\"{text[evaluator.position..]}\" is left over");
            return new Truth(holds, [.. evaluator.doubts.Distinct()]);
        }
        catch (FormatException e)
        {
            return new Truth(Holds: false, [new Doubt(at, $"the condition \"{text.Trim()}\" isn't evaluated: {e.Message}")]);
        }
    }

    // Both sides of "or" and "and" are always evaluated, never short-circuited, so that every doubt in the condition is known
    // whichever way it is read.
    private bool Or()
    {
        var holds = And();
        while (Word("or"))
            holds = And() | holds;
        return holds;
    }

    private bool And()
    {
        var holds = Not();
        while (Word("and"))
            holds = Not() & holds;
        return holds;
    }

    private bool Not() => Symbol("!") ? !Not() : Comparison();

    private bool Comparison()
    {
        Space();
        if (Symbol("("))
        {
            var inner = Or();
            if (!Symbol(")"))
                throw new FormatException("a parenthesis isn't closed");
            return inner;
        }

        if (Function() is { } called)
            return called;
        var left = Operand();
        foreach (var op in (string[])["==", "!=", "<=", ">=", "<", ">"])
        {
            if (Symbol(op))
                return Compare(left, op, Operand());
        }

        return Boolean(left);
    }

    /// <summary><c>Exists('…')</c> or <c>HasTrailingSlash('…')</c>, or null when the next word isn't a function's.</summary>
    private bool? Function()
    {
        Space();
        var start = position;
        while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] == '_'))
            position++;
        var name = text[start..position];
        Space();
        if (name.Length == 0 || !Symbol("("))
        {
            position = start;
            return null;
        }

        var argument = Symbol(")") ? null : Operand();
        if (argument is not null && !Symbol(")"))
            throw new FormatException($"{name}( takes one argument here");
        if (argument is not null && name.Equals("Exists", StringComparison.OrdinalIgnoreCase))
            return argument.Length > 0 && DiskPath.Find(folder, argument, out _) is not null;
        if (argument is not null && name.Equals("HasTrailingSlash", StringComparison.OrdinalIgnoreCase))
            return argument.EndsWith('/') || argument.EndsWith('\\');
        doubts.Add(new Doubt(at, $"the condition function {name}() isn't evaluated"));
        return false;
    }

    /// <summary>A quoted value or a bare one (a word, a number, a <c>$(…)</c>), expanded.</summary>
    private string Operand()
    {
        Space();
        if (position >= text.Length)
            throw new FormatException("a value is missing");
        string raw;
        if (text[position] == '\'')
        {
            var end = position + 1;
            while (end < text.Length && text[end] != '\'')
            {
                // A $(…) inside the quotes may hold quotes of its own, in a property function's arguments.
                if (text[end] is '$' or '@' or '%' && end + 1 < text.Length && text[end + 1] == '(' && ProjectProperties.Closing(text, end + 1) is var close and > 0)
                    end = close + 1;
                else
                    end++;
            }

            if (end >= text.Length)
                throw new FormatException("a quote isn't closed");
            raw = text[(position + 1)..end];
            position = end + 1;
        }
        else if (text[position] is '$' or '@' or '%' && position + 1 < text.Length && text[position + 1] == '(')
        {
            var close = ProjectProperties.Closing(text, position + 1);
            if (close < 0)
                throw new FormatException("a parenthesis isn't closed");
            raw = text[position..(close + 1)];
            position = close + 1;
        }
        else
        {
            var start = position;
            while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] is '.' or '_' or '-'))
                position++;
            if (position == start)
                throw new FormatException($"\"{text[position..]}\" isn't a value");
            raw = text[start..position];
        }

        var value = expand(raw);
        doubts.AddRange(value.Doubts);
        return value.Text;
    }

    private bool Compare(string left, string op, string right)
    {
        switch (op)
        {
            case "==":
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            case "!=":
                return !string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        if (!double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var l) || !double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
        {
            doubts.Add(new Doubt(at, $"'{left}' {op} '{right}' compares values that aren't numbers, which isn't evaluated"));
            return false;
        }

        return op switch
        {
            "<" => l < r,
            ">" => l > r,
            "<=" => l <= r,
            _ => l >= r,
        };
    }

    /// <summary>A value standing alone as a condition: MSBuild's words for true and false.</summary>
    private bool Boolean(string value)
    {
        switch (value.ToUpperInvariant())
        {
            case "TRUE" or "ON" or "YES":
                return true;
            case "FALSE" or "OFF" or "NO":
                return false;
            default:
                doubts.Add(new Doubt(at, $"'{value}' stands alone in a condition but isn't true or false"));
                return false;
        }
    }

    private bool Word(string word)
    {
        Space();
        if (position + word.Length <= text.Length
            && string.Compare(text, position, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0
            && (position + word.Length == text.Length || !char.IsAsciiLetterOrDigit(text[position + word.Length])))
        {
            position += word.Length;
            return true;
        }

        return false;
    }

    private bool Symbol(string symbol)
    {
        Space();
        if (string.CompareOrdinal(text, position, symbol, 0, symbol.Length) != 0)
            return false;
        // "!" alone, not the start of "!=".
        if (symbol == "!" && position + 1 < text.Length && text[position + 1] == '=')
            return false;
        position += symbol.Length;
        return true;
    }

    private void Space()
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
            position++;
    }
}
