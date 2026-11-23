namespace Haruspex.SqlServer.Model;

/// <summary>The statements that change an object after its definition in a way the model does not apply (<see cref="Redefinition"/>).</summary>
public enum RedefiningStatement
{
    /// <summary><c>ALTER TABLE</c> that can change a column (not one that only adds or checks constraints).</summary>
    AlterTable,

    /// <summary><c>DROP TABLE</c>.</summary>
    DropTable,

    /// <summary><c>ALTER PROCEDURE</c>.</summary>
    AlterProcedure,

    /// <summary><c>DROP PROCEDURE</c>.</summary>
    DropProcedure,

    /// <summary><c>ALTER FUNCTION</c>.</summary>
    AlterFunction,

    /// <summary><c>DROP FUNCTION</c>.</summary>
    DropFunction,

    /// <summary>A second <c>CREATE VIEW</c> of one view.</summary>
    CreateView,

    /// <summary><c>ALTER VIEW</c>.</summary>
    AlterView,

    /// <summary><c>CREATE OR ALTER VIEW</c>.</summary>
    CreateOrAlterView,

    /// <summary><c>DROP VIEW</c>, under a condition that may be false, between two definitions of a view in one script.</summary>
    DropView,

    /// <summary>A <c>CREATE TABLE</c> that isn't the table's definition: nested in another statement, or in a deployment script.</summary>
    CreateTable,

    /// <summary><c>CREATE PROCEDURE</c> in a deployment script.</summary>
    CreateProcedure,

    /// <summary><c>CREATE OR ALTER PROCEDURE</c> in a deployment script.</summary>
    CreateOrAlterProcedure,

    /// <summary><c>CREATE FUNCTION</c> in a deployment script.</summary>
    CreateFunction,

    /// <summary><c>CREATE OR ALTER FUNCTION</c> in a deployment script.</summary>
    CreateOrAlterFunction,

    /// <summary>A deployment script that can't be read in full, and names the object: what it does to it isn't known.</summary>
    UnreadScript,
}
