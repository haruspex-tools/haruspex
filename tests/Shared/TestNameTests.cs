using System.Reflection;

namespace Haruspex.TestSupport;

/// <summary>
/// Every test's name is a sentence saying what it proves, at least three words joined by underscores, such as
/// <c>A_key_inside_a_view_bounds_its_json</c>. A one-word name such as <c>Ir</c> says which code runs, not what holds,
/// and a reader of a failure learns nothing from it.
/// </summary>
/// <remarks>
/// A test rather than an analyser rule: the SDK's naming rules (IDE1006) choose symbols by kind, accessibility and
/// modifiers, never by attribute, so none can tell a test from any other public method, and an analyser package for one
/// check would be a dependency of its own. Compiled into both test projects from <c>tests/Shared</c>, so each checks its
/// own assembly.
/// </remarks>
public class TestNameTests
{
    private const int FewestWords = 3;

    [Fact]
    public void Every_test_name_is_a_sentence_of_at_least_three_words()
    {
        var offenders = typeof(TestNameTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.IsDefined(typeof(FactAttribute), inherit: true)) // a [Theory] is a [Fact] too
            .Where(m => m.Name.Split('_') is var words && (words.Length < FewestWords || words.Any(w => w.Length == 0)))
            .Select(m => $"{m.DeclaringType!.FullName}.{m.Name}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Name each test for what it proves, in at least {FewestWords} words joined by single underscores:\n" + string.Join("\n", offenders));
    }
}
