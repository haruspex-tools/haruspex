namespace Haruspex.TestSupport;

/// <summary>
/// The laws every equality must keep, checked over a sample: reflexive, symmetric and transitive, and equal values hash
/// equal. A fixed point, a cache or a join that compares values rests on them (decision 0022), so a type whose equality is
/// written by hand is held to them by a test that calls this.
/// </summary>
internal static class EqualityLaws
{
    /// <summary>Asserts the laws over every pair and triple of <paramref name="sample"/>, which should hold equal values built apart.</summary>
    public static void Hold<T>(IReadOnlyList<T> sample)
        where T : notnull
    {
        Assert.Contains(sample, a => sample.Any(b => !ReferenceEquals(a, b) && a.Equals(b)));
        Assert.Contains(sample, a => sample.Any(b => !a.Equals(b)));
        foreach (var a in sample)
        {
            Assert.True(a.Equals(a), $"Not reflexive: {a}");
            foreach (var b in sample)
            {
                Assert.True(a.Equals(b) == b.Equals(a), $"Not symmetric: {a} and {b}");
                if (!a.Equals(b))
                    continue;
                Assert.True(a.GetHashCode() == b.GetHashCode(), $"Equal but hashed apart: {a} and {b}");
                foreach (var c in sample.Where(c => b.Equals(c)))
                    Assert.True(a.Equals(c), $"Not transitive: {a}, {b} and {c}");
            }
        }
    }
}
