-- Recursion through its caller's #counts: each step adds the rack counts (null for an oven without racks), and the last
-- returns what the table holds then, after the steps before it wrote to it.
CREATE PROCEDURE calls.recount @n int AS
SET NOCOUNT ON;
IF @n <= 0
    SELECT c.x FROM #counts AS c ORDER BY c.x;
ELSE
BEGIN
    INSERT INTO #counts (x) SELECT o.rack_count FROM calls.oven AS o;
    DECLARE @m int = @n - 1;
    EXEC calls.recount @m;
END
