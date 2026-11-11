namespace Haruspex.Emit.Zod;

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
