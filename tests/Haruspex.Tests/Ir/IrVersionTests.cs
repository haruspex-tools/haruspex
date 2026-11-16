using System.Text.Json;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// The IR file kept from each version of the format (<c>tests/ir-versions</c>): the current version's is read and round trips,
/// and every older one is refused with a message that names its version, the version read and where in the file.
/// </summary>
public class IrVersionTests
{
    private static string Folder => Path.Combine(RepositoryPaths.Root, "tests", "ir-versions");

    public static TheoryData<string> Older { get; } =
        [.. Versions().Where(v => !IrDocument.ReadableVersions.Contains(v))];

    [Fact]
    public void There_is_a_file_for_every_version_up_to_the_current_one()
    {
        var current = Version.Parse(IrDocument.CurrentVersion);

        Assert.Equal(
            Enumerable.Range(1, current.Minor).Select(minor => $"0.{minor}"),
            Versions().OrderBy(Version.Parse));
    }

    [Fact]
    public void Each_file_is_named_for_the_version_it_holds() =>
        Assert.All(Versions(), version =>
        {
            using var json = JsonDocument.Parse(File.ReadAllText(FileOf(version)));
            Assert.Equal(version, json.RootElement.GetProperty("irVersion").GetString());
        });

    [Fact]
    public void The_current_version_s_file_is_read_and_writes_back_byte_for_byte()
    {
        var text = File.ReadAllText(FileOf(IrDocument.CurrentVersion));

        Assert.Equal(text, IrJsonWriter.Write(IrJsonReader.Read(text)));
    }

    [Fact]
    public void The_reader_reads_the_current_version_only() => Assert.Equal([IrDocument.CurrentVersion], IrDocument.ReadableVersions);

    [Theory]
    [MemberData(nameof(Older))]
    public void An_older_version_s_file_is_refused_naming_both_versions(string version)
    {
        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(File.ReadAllText(FileOf(version))));

        Assert.Equal("$.irVersion", e.Path);
        Assert.Equal(
            $"$.irVersion is \"{version}\", and this reader reads IR 0.22 only. Run haruspex generate again to write the file in this version.",
            e.Message);
    }

    private static string FileOf(string version) => Path.Combine(Folder, version + ".ir.json");

    private static IEnumerable<string> Versions() =>
        Directory.EnumerateFiles(Folder, "*.ir.json").Select(f => Path.GetFileName(f)[..^".ir.json".Length]).Order(StringComparer.Ordinal);
}
