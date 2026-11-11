CREATE PROCEDURE sales.list_customer_orders
AS
SET NOCOUNT ON;

SELECT c.customer_id AS customerId,
       c.name,
       c.email,
       c.region_code AS [address.region],
       -- A FOR JSON subquery embeds as JSON without json_query.
       (
           SELECT o.order_id AS id,
                  o.placed_at AS placedAt,
                  o.shipped_at AS shippedAt,
                  o.total
           FROM sales.[order] AS o
           WHERE o.customer_id = c.customer_id
           FOR JSON PATH
       ) AS orders
FROM sales.customer AS c
ORDER BY c.customer_id
FOR JSON PATH, INCLUDE_NULL_VALUES;
