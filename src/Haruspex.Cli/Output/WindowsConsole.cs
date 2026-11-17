using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Haruspex.Cli.Output;

/// <summary>
/// Turns on a Windows console's processing of escape sequences (<c>ENABLE_VIRTUAL_TERMINAL_PROCESSING</c>), which colour and
/// links need. Windows Terminal and Visual Studio Code's terminal have it on; the classic console host, Windows 10's default
/// for <c>cmd</c>, has it off, and .NET doesn't turn it on, so there <c>ESC[1;33m</c> would be shown as text. Where it can't be
/// turned on, the stream is not coloured (<see cref="Terminal.OutputShowsEscapes"/>).
/// </summary>
/// <remarks>
/// <c>LibraryImport</c>, whose marshalling is generated at compile time, so Native AOT compiles it as it is; the generated code
/// for the <c>out</c> parameter is why the command's project allows unsafe code. Looked for in System32 alone, so no
/// <c>kernel32.dll</c> beside the binary is loaded in its place.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static partial class WindowsConsole
{
    /// <summary>The standard output's handle, for <see cref="ShowsEscapes"/>.</summary>
    public const int Output = -11;

    /// <summary>The standard error's handle.</summary>
    public const int Error = -12;

    private const uint VirtualTerminalProcessing = 0x0004;

    /// <summary>Whether the console behind the standard handle <paramref name="handle"/> processes escape sequences, turning that on if it can.</summary>
    public static bool ShowsEscapes(int handle)
    {
        var console = GetStdHandle(handle);
        if (console == 0 || console == -1 || !GetConsoleMode(console, out var mode))
            return false;
        return (mode & VirtualTerminalProcessing) != 0 || SetConsoleMode(console, mode | VirtualTerminalProcessing);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetStdHandle(int handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint console, out uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint console, uint mode);
}
