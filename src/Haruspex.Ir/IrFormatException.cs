namespace Haruspex.Ir;

/// <summary>
/// An IR file that <see cref="IrJsonReader"/> refuses: not JSON, not an IR document, a version this reader doesn't read,
/// or a part that breaks the format. It names where, so the file can be fixed or regenerated.
/// </summary>
public sealed class IrFormatException : Exception
{
    /// <summary>Refuses a document, without saying where (the standard constructor; the reader always says where).</summary>
    public IrFormatException()
        : this("$", "is not an IR document.")
    {
    }

    /// <summary>Refuses a whole document for <paramref name="message"/> (the standard constructor).</summary>
    public IrFormatException(string message)
        : this("$", message)
    {
    }

    /// <summary>Refuses a whole document for <paramref name="message"/>, caused by <paramref name="innerException"/>.</summary>
    public IrFormatException(string message, Exception innerException)
        : this("$", message, innerException)
    {
    }

    /// <summary>Refuses the part of the document at <paramref name="path"/> for <paramref name="problem"/>.</summary>
    /// <param name="path">Where in the document, as a JSONPath from its root: <c>$.objects[0].results[1].cardinality</c>.</param>
    /// <param name="problem">What is wrong there, as a phrase that follows the path: <c>is missing</c>.</param>
    public IrFormatException(string path, string problem)
        : base($"{path} {problem}")
    {
        Path = path;
        Problem = problem;
    }

    /// <summary>Refuses a document that is not JSON at all.</summary>
    /// <param name="path">Where the JSON parser stopped, as a JSONPath.</param>
    /// <param name="problem">What the parser found.</param>
    /// <param name="inner">The parser's own exception.</param>
    public IrFormatException(string path, string problem, Exception inner)
        : base($"{path} {problem}", inner)
    {
        Path = path;
        Problem = problem;
    }

    /// <summary>Where in the document the problem is, as a JSONPath from its root (<c>$</c>).</summary>
    public string Path { get; }

    /// <summary>What is wrong there.</summary>
    public string Problem { get; }
}
