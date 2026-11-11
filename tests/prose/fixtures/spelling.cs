// cspell reads C# strings, which Vale doesn't: the diagnostics are in them. Only the strings are wrong here.
namespace Haruspex.Fixture;

public static class Spelling
{
    public static string Message => "the name could not be normalized, so its behavior is unknown";

    public static string Fine => "the name could not be normalised, so its behaviour is unknown";

    public static string Typo => "the recieved value";
}
