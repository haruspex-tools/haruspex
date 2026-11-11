-- A staged report: the sales are selected into a temporary table, which is narrowed and updated before it is
-- returned as JSON. Each column keeps what SELECT … INTO gave it (the check on amount, the left join's nulls), and
-- the update joins what it writes into note.
CREATE PROCEDURE flow.region_report
    @channel varchar(6) = NULL
AS
SET NOCOUNT ON;

DROP TABLE IF EXISTS #sale;

SELECT s.sale_id, s.amount, s.channel, s.note, r.code AS region_code, r.name AS region_name
INTO #sale
FROM flow.sale AS s
LEFT JOIN flow.region AS r ON r.region_id = s.region_id;

IF @channel IS NOT NULL
    DELETE FROM #sale WHERE channel <> @channel;

-- Phone sales are noted with their region's name, which may be missing.
UPDATE x SET note = x.region_name
FROM #sale AS x
WHERE x.channel = 'phone';

SELECT x.sale_id AS id, x.amount, x.channel, x.note, x.region_code AS [region.code], x.region_name AS [region.name]
FROM #sale AS x
ORDER BY x.sale_id
FOR JSON PATH;
