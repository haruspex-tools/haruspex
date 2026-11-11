using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// A scalar says "nothing more is known" in one way only, so that two scalars making the same claim are equal. The writer
/// never wrote empty facets, so when scalars were compared by their JSON, empty facets and none were already the same.
/// </summary>
public class IrScalarTests
{
    private static readonly IrScalar Int = new(IrScalarKind.Number, IrFormats.Int32, "int");

    [Fact]
    public void Empty_facets_are_kept_as_none_since_they_claim_nothing()
    {
        var empty = new IrScalar(IrScalarKind.Number, IrFormats.Int32, "int", new IrFacets());

        Assert.Null(empty.Facets);
        Assert.Equal(Int, empty);
        Assert.Equal(Int.GetHashCode(), empty.GetHashCode());
        Assert.Equal(Int, Int with { Facets = new IrFacets() });
    }

    [Fact]
    public void Facets_that_claim_something_are_kept_and_compared()
    {
        var length = new IrFacets(Length: new IrLength(10, false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, new IrFactSource(IrFactSources.Type, "nvarchar(10)")));
        var text = new IrScalar(IrScalarKind.String, null, "nvarchar(10)", length);

        Assert.Same(length, text.Facets);
        Assert.Equal(text, new IrScalar(IrScalarKind.String, null, "nvarchar(10)", length with { }));
        Assert.NotEqual(text, text with { Facets = null });
    }
}
