namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A place the state traces JSON built here in (<see cref="JsonSlot"/>): a variable of type <c>nvarchar(max)</c>
/// (<see cref="VariableSlot"/>), or such a column of a table variable (<see cref="ColumnSlot"/>). Each compares as SQL
/// Server compares the names it is made of, ignoring case.
/// </summary>
internal abstract record JsonSlotKey
{
    private protected JsonSlotKey()
    {
    }
}
