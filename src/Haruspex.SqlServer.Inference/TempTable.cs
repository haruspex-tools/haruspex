namespace Haruspex.SqlServer.Inference;

// What the statement walk (ProcedureAnalyzer.Flow.cs) carries from one statement to the next, and how two states join
// where paths meet. README.md in this folder gives the model and why each join is sound.

/// <summary>What is known of one temporary table at a point in the procedure.</summary>
internal abstract record TempTable;
