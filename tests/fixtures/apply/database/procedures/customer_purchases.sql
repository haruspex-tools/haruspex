-- CROSS APPLY of a query that may return no row drops the customer (customer 2); its columns keep their own nullability.
CREATE PROCEDURE mart.customer_purchases
AS
SET NOCOUNT ON;

SELECT c.customer_id,
       c.name,
       x.purchase_id,
       x.placed_on,
       x.note
FROM mart.customer AS c
CROSS APPLY (
    SELECT p.purchase_id, p.placed_on, p.note
    FROM mart.purchase AS p
    WHERE p.customer_id = c.customer_id
) AS x
ORDER BY c.customer_id, x.purchase_id;
