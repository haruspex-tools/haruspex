-- OUTER APPLY of TOP (1): every customer, the last purchase's columns null for a customer without one (outer-apply),
-- whatever their declarations say.
CREATE PROCEDURE mart.last_purchase
AS
SET NOCOUNT ON;

SELECT c.customer_id,
       c.name,
       x.purchase_id,
       x.placed_on,
       x.status
FROM mart.customer AS c
OUTER APPLY (
    SELECT TOP (1) p.purchase_id, p.placed_on, p.status
    FROM mart.purchase AS p
    WHERE p.customer_id = c.customer_id
    ORDER BY p.placed_on DESC, p.purchase_id DESC
) AS x
ORDER BY c.customer_id;
