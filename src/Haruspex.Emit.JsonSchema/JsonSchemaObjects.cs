namespace Haruspex.Emit.JsonSchema;

/// <summary>How result objects treat keys their type does not list.</summary>
public enum JsonSchemaObjects
{
    /// <summary>Other keys are allowed: a column the database adds before the application redeploys doesn't fail it. The default.</summary>
    Strip,

    /// <summary><c>"additionalProperties": false</c>: an unknown key fails validation.</summary>
    Strict,
}
