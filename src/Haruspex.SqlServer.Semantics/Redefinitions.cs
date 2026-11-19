using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>What a <see cref="Redefinition"/> means for a reader, in words: the reason an object's definition is not trusted.</summary>
public static class Redefinitions
{
    /// <summary>Why the object's definition cannot be trusted, in a sentence.</summary>
    /// <param name="redefinition">The statement that changes the object after its definition.</param>
    public static string Why(Redefinition redefinition)
    {
        ArgumentNullException.ThrowIfNull(redefinition);
        return $"{redefinition.Name} is changed by {Statement(redefinition.Statement)} at {redefinition.At}, and which definition applies depends on the order the scripts run.";
    }

    /// <summary>The statement as T-SQL writes it, in capitals: <c>ALTER TABLE</c>, <c>DROP PROCEDURE</c>.</summary>
    public static string Statement(RedefiningStatement statement) => statement switch
    {
        RedefiningStatement.AlterTable => "ALTER TABLE",
        RedefiningStatement.DropTable => "DROP TABLE",
        RedefiningStatement.AlterProcedure => "ALTER PROCEDURE",
        RedefiningStatement.DropProcedure => "DROP PROCEDURE",
        RedefiningStatement.AlterFunction => "ALTER FUNCTION",
        RedefiningStatement.DropFunction => "DROP FUNCTION",
        RedefiningStatement.CreateView => "CREATE VIEW",
        RedefiningStatement.AlterView => "ALTER VIEW",
        RedefiningStatement.CreateOrAlterView => "CREATE OR ALTER VIEW",
        RedefiningStatement.DropView => "DROP VIEW",
    };
}
