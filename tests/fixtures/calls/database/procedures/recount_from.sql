-- The caller of calls.recount: #counts starts with one value, never null.
CREATE PROCEDURE calls.recount_from @n int AS
SET NOCOUNT ON;
DECLARE @start int = 1;
SELECT @start AS x INTO #counts;
EXEC calls.recount @n;
