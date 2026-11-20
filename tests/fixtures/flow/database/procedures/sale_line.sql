-- A temporary table SELECT … INTO made, keyed afterwards by ALTER TABLE … ADD PRIMARY KEY: from there on a query that fixes the
-- key reads at most one row, so the line comes back as one JSON object, or null when there is no such sale, and as a rowset of
-- at most one row.
CREATE PROCEDURE flow.sale_line
    @sale_id int
AS
SET NOCOUNT ON;

SELECT s.sale_id, s.region_id, s.amount, s.channel
INTO #lines
FROM flow.sale AS s;

ALTER TABLE #lines ADD PRIMARY KEY (sale_id);

SELECT (
    SELECT l.sale_id, l.amount, l.channel
    FROM #lines AS l
    WHERE l.sale_id = @sale_id
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
) AS line;

SELECT l.region_id, l.amount FROM #lines AS l WHERE l.sale_id = @sale_id;
