namespace Haruspex.Config;

/// <summary>How result objects treat keys their type does not list (<c>"objects"</c>).</summary>
public enum ObjectsPolicy
{
    /// <summary>Unknown keys are removed when parsing (<c>z.object</c>). The default, for deployment safety.</summary>
    Strip,

    /// <summary>An unknown key fails parsing (<c>z.strictObject</c>).</summary>
    Strict,
}
