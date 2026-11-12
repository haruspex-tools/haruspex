using Haruspex.Emit.Zod;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>The names TypeScript keeps for itself, which the emitter refuses as a binding and the CLI hands to the config.</summary>
public class TypeScriptNamesTests
{
    [Theory]
    [InlineData("string")]
    [InlineData("delete")]
    [InlineData("class")]
    [InlineData("eval")]
    public void A_reserved_word_or_built_in_type_name_is_reserved(string word) => Assert.True(TypeScriptNames.IsReserved(word));

    [Theory]
    [InlineData("String")]
    [InlineData("Delete")]
    [InlineData("order")]
    public void A_name_in_another_case_or_not_kept_is_free(string word) => Assert.False(TypeScriptNames.IsReserved(word));
}
