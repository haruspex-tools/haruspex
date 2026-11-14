using System.Data.SqlTypes;
using Microsoft.SqlServer.Server;
public static class Kiln
{
    [SqlFunction(IsDeterministic = true)]
    public static SqlInt32 Double(SqlInt32 x) { return x * 2; }

    [SqlProcedure]
    public static void Report() { SqlContext.Pipe.Send("fired"); }
}
