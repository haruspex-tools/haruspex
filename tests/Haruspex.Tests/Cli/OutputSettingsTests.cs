using Haruspex.Cli;
using Haruspex.Cli.Output;

namespace Haruspex.Tests.Cli;

/// <summary>
/// When a stream is coloured, when its codes are links and when a progress line is drawn: each convention the command follows
/// (the colour option, <c>NO_COLOR</c>, <c>FORCE_COLOR</c>, <c>FORCE_HYPERLINK</c>, <c>CI</c>, <c>TERM=dumb</c>), as a row.
/// </summary>
public class OutputSettingsTests
{
    private static Terminal In(bool isTerminal, params string[] variables) => new()
    {
        OutputIsTerminal = isTerminal,
        ErrorIsTerminal = isTerminal,
        Environment = variables.Select(v => v.Split('=', 2)).ToDictionary(p => p[0], p => p[1]),
    };

    [Theory]
    [InlineData("Auto", true, new string[0], true)]
    [InlineData("Auto", false, new string[0], false)]
    [InlineData("Auto", true, new[] { "NO_COLOR=1" }, false)]
    [InlineData("Auto", true, new[] { "NO_COLOR=" }, true)]
    [InlineData("Auto", false, new[] { "FORCE_COLOR=1" }, true)]
    [InlineData("Auto", true, new[] { "FORCE_COLOR=0" }, false)]
    [InlineData("Auto", true, new[] { "FORCE_COLOR=false" }, false)]
    [InlineData("Auto", true, new[] { "FORCE_COLOR=1", "NO_COLOR=1" }, true)]
    [InlineData("Auto", true, new[] { "TERM=dumb" }, false)]
    [InlineData("Always", false, new[] { "NO_COLOR=1" }, true)]
    [InlineData("Never", true, new[] { "FORCE_COLOR=1" }, false)]
    public void A_stream_is_coloured_as_the_option_then_the_environment_then_the_terminal_say(string choice, bool isTerminal, string[] variables, bool coloured) =>
        Assert.Equal(coloured, OutputSettings.Settle(OutputDetail.Normal, Enum.Parse<ColourChoice>(choice), In(isTerminal, variables)).Output.Colour);

    [Theory]
    [InlineData(true, new[] { "TERM_PROGRAM=WezTerm" }, true)]
    [InlineData(true, new[] { "TERM_PROGRAM=iTerm.app" }, true)]
    [InlineData(true, new[] { "WT_SESSION=1" }, true)]
    [InlineData(true, new[] { "VTE_VERSION=7600" }, true)]
    [InlineData(true, new[] { "VTE_VERSION=4600" }, false)]
    [InlineData(true, new[] { "TERM_PROGRAM=Apple_Terminal" }, false)]
    [InlineData(true, new string[0], false)]
    [InlineData(true, new[] { "TERM_PROGRAM=WezTerm", "CI=true" }, false)]
    [InlineData(true, new[] { "TERM_PROGRAM=WezTerm", "FORCE_HYPERLINK=0" }, false)]
    [InlineData(true, new[] { "FORCE_HYPERLINK=1" }, true)]
    [InlineData(false, new[] { "TERM_PROGRAM=WezTerm", "FORCE_COLOR=1" }, false)]
    [InlineData(true, new[] { "TERM_PROGRAM=WezTerm", "NO_COLOR=1" }, false)]
    public void Codes_are_links_only_with_colour_in_a_terminal_known_to_show_them(bool isTerminal, string[] variables, bool linked) =>
        Assert.Equal(linked, OutputSettings.Settle(OutputDetail.Normal, ColourChoice.Auto, In(isTerminal, variables)).Error.Links);

    [Theory]
    [InlineData("Normal", true, new string[0], true)]
    [InlineData("Normal", false, new string[0], false)]
    [InlineData("Normal", true, new[] { "CI=true" }, false)]
    [InlineData("Normal", true, new[] { "TERM=dumb" }, false)]
    [InlineData("Quiet", true, new string[0], false)]
    [InlineData("Verbose", true, new[] { "NO_COLOR=1" }, true)]
    public void Progress_is_drawn_only_on_a_terminal_outside_ci(string detail, bool isTerminal, string[] variables, bool shown) =>
        Assert.Equal(shown, OutputSettings.Settle(Enum.Parse<OutputDetail>(detail), ColourChoice.Auto, In(isTerminal, variables)).Progress);

    [Fact]
    public void Each_stream_is_settled_by_whether_it_is_a_terminal()
    {
        var settings = OutputSettings.Settle(OutputDetail.Normal, ColourChoice.Auto, new Terminal { OutputIsTerminal = false, ErrorIsTerminal = true });

        Assert.Equal((false, true), (settings.Output.Colour, settings.Error.Colour));
    }
}
