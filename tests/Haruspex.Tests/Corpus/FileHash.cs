using System.Security.Cryptography;

namespace Haruspex.Tests.Corpus;

/// <summary>The SHA-256 of a file, as the corpus manifests record it: lower-case hex.</summary>
internal static class FileHash
{
    /// <summary>The file's SHA-256, in lower-case hex.</summary>
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
