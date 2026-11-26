namespace Haruspex.SqlServer.Model;

/// <summary>What a statement does with an extended property.</summary>
public enum PropertyAction
{
    /// <summary><c>sp_addextendedproperty</c>, or a package's element.</summary>
    Add,

    /// <summary><c>sp_updateextendedproperty</c>.</summary>
    Update,

    /// <summary><c>sp_dropextendedproperty</c>.</summary>
    Drop,
}
