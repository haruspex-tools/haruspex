namespace Haruspex.Emit.Zod;

/// <summary>
/// Settings for the Zod emitter. The emitter never reads the config file: whoever composes the tool (the CLI) maps the
/// config's settings to these.
/// </summary>
public sealed record ZodOptions
{
    /// <summary>How result objects treat keys their type does not list.</summary>
    public ZodObjects Objects { get; init; } = ZodObjects.Strip;
}

/// <summary>How result objects treat keys their type does not list.</summary>
public enum ZodObjects
{
    /// <summary><c>z.object</c>: unknown keys are removed when parsing. The default.</summary>
    Strip,

    /// <summary><c>z.strictObject</c>: an unknown key fails parsing.</summary>
    Strict,
}

/// <summary>The document cannot be written as Zod: a name is not an identifier, or an imported name clashes.</summary>
public sealed class ZodEmitException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ZodEmitException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public ZodEmitException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and its cause.</summary>
    public ZodEmitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
