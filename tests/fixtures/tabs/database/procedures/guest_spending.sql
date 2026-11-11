-- Money totals per guest: COALESCE(SUM(…), 0) is never null, converted to decimal(19,2) it may overflow; a guest without
-- tabs has a count of 0 and no best tip.
CREATE PROCEDURE cafe.guest_spending
AS
SET NOCOUNT ON;

SELECT g.guest_id,
       COUNT_BIG(t.tab_id) AS tabs,
       CONVERT(decimal(19, 2), COALESCE(SUM(s.subtotal), 0)) AS spent,
       MAX(t.tip) AS best_tip,
       COALESCE(MAX(t.table_no), 0) AS last_table
FROM cafe.guest AS g
LEFT JOIN cafe.tab AS t ON t.guest_id = g.guest_id
OUTER APPLY cafe.tab_totals(t.tab_id) AS s
GROUP BY g.guest_id
ORDER BY g.guest_id;
