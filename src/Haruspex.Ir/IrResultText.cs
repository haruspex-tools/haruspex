namespace Haruspex.Ir;

/// <summary>
/// A result in words, for the summary an emitter writes beside a procedure's schema: <c>rows</c>, or <c>JSON, null when no rows
/// match</c>. It reads only the IR's own claims (the encoding and <see cref="IrResult.AbsentBecause"/>), so every emitter says
/// the same thing of the same result, each in its own place (a Zod comment, a JSON Schema description).
/// </summary>
public static class IrResultText
{
    /// <summary>
    /// What a result is, and what it reads as when absent, by the reader conventions in README.md ("Results"): a FOR JSON query
    /// that matched no rows, or a statement that did not run (in a branch, or after a RETURN), whose result set is then missing
    /// altogether; or a result the config file declares may be absent, the declaration its reason.
    /// </summary>
    /// <param name="result">The result.</param>
    public static string Describe(IrResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var noRows = result.AbsentBecause.Any(r => r.Rule == IrNullRule.NoRows);
        var notRun = result.AbsentBecause.Any(r => r.Rule == IrNullRule.NotRun);
        var when = (noRows, notRun) switch
        {
            (true, true) => "when no rows match or it does not run",
            (true, false) => "when no rows match",
            (false, true) => "when it does not run",
            _ => null,
        };
        var what = result.Encoding == IrEncoding.Rowset ? "rows" : "JSON";
        var absent = result.Encoding == IrEncoding.Rowset ? "[]" : "null";

        // A result the config file declares may be absent says so, but not why: the declaration is the reason.
        var declared = result.AbsentBecause.Any(r => r.Rule == IrNullRule.Declared);
        return (when, declared) switch
        {
            (null, false) => what,
            (null, true) => $"{what}, {absent} when absent, as the config file declares",
            (_, false) => $"{what}, {absent} {when}",
            (_, true) => $"{what}, {absent} {when}, or when absent, as the config file declares",
        };
    }
}
