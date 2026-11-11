namespace Haruspex.Config;

/// <summary>The case style of generated names (<c>naming.case</c>).</summary>
public enum NamingCase
{
    /// <summary><c>GetOrder</c>: every word capitalised; <c>$</c>, <c>#</c> and <c>_</c> break words. The default.</summary>
    Pascal,

    /// <summary><c>getOrder</c>: as Pascal, with the first letter lower case.</summary>
    Camel,

    /// <summary>The name as it is after the replacements, with any prefix: it must be an identifier already.</summary>
    Preserve,
}
