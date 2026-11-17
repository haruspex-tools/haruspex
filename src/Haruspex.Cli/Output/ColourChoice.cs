namespace Haruspex.Cli.Output;

/// <summary>What the colour option asks for.</summary>
internal enum ColourChoice
{
    /// <summary>Colour when the stream is a terminal, as the environment allows (<see cref="OutputSettings.Coloured"/>).</summary>
    Auto,

    /// <summary>Colour always, whatever the stream and the environment: for a pager that shows it, such as <c>less -R</c>.</summary>
    Always,

    /// <summary>Never colour.</summary>
    Never,
}
