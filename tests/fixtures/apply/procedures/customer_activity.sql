-- OUTER APPLY of an ungrouped aggregate is one row for every customer: the count is never null and the last date is null
-- only as an empty aggregate. Its WHERE rejects a customer without an email, but the aggregate still returns its row,
-- reading c.email as it is (null), so the WHERE proves nothing of contact. A filter on the right side's own column proves
-- it outside: the second result keeps only customers with purchases, whose last date is then never null.
CREATE PROCEDURE mart.customer_activity
AS
SET NOCOUNT ON;

SELECT c.customer_id,
       a.contact,
       a.purchases,
       a.last_placed
FROM mart.customer AS c
OUTER APPLY (
    SELECT c.email AS contact, COUNT_BIG(*) AS purchases, MAX(p.placed_on) AS last_placed
    FROM mart.purchase AS p
    WHERE c.email IS NOT NULL AND p.customer_id = c.customer_id
) AS a
ORDER BY c.customer_id;

SELECT c.customer_id,
       a.last_placed
FROM mart.customer AS c
OUTER APPLY (
    SELECT MAX(p.placed_on) AS last_placed
    FROM mart.purchase AS p
    WHERE p.customer_id = c.customer_id
) AS a
WHERE a.last_placed IS NOT NULL
ORDER BY c.customer_id;
