-- A CTE, a derived table and VALUES are tables to AUTO, each nesting under the one before it; OPENJSON is not, so its column
-- goes where it stands, in the deepest object so far. An order with no lines keeps its object in c's array, OUTER APPLY
-- keeping its row, without line_count.
CREATE PROCEDURE nest.other_sources
AS
SET NOCOUNT ON;

WITH counted AS (SELECT l.order_id, COUNT(*) AS line_count FROM nest.order_lines AS l GROUP BY l.order_id)
SELECT o.order_id, c.line_count, d.first_sku, j.tag, v.flag
FROM nest.orders AS o
OUTER APPLY (SELECT k.line_count FROM counted AS k WHERE k.order_id = o.order_id) AS c
OUTER APPLY (SELECT MIN(l.sku) AS first_sku FROM nest.order_lines AS l WHERE l.order_id = o.order_id) AS d
CROSS APPLY OPENJSON(N'[{"tag":"t"}]') WITH (tag varchar(5)) AS j
CROSS JOIN (VALUES (CAST(1 AS bit))) AS v (flag)
ORDER BY o.order_id
FOR JSON AUTO;
