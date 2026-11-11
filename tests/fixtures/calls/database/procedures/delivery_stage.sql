-- INSERT … EXEC without a column list into a table SELECT … INTO made with an identity column: SQL Server copies the
-- identity, and the insert skips it, so rack_sizes' rack counts go into n, which may then be null.
CREATE PROCEDURE calls.delivery_stage AS
SET NOCOUNT ON;
DECLARE @first int = 1;
SELECT d.delivery_id, @first AS n INTO #stage FROM calls.delivery AS d;
INSERT INTO #stage EXEC calls.rack_counts;
SELECT s.delivery_id, s.n FROM #stage AS s ORDER BY s.delivery_id;
