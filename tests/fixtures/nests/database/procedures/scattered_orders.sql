-- Lines ordered by sku, so an order's lines are apart and the order is written once for each run of them: AUTO merges only
-- adjacent rows, so the same order may appear twice.
CREATE PROCEDURE nest.scattered_orders
AS
SET NOCOUNT ON;

SELECT o.order_id, l.sku
FROM nest.orders AS o
JOIN nest.order_lines AS l ON l.order_id = o.order_id
ORDER BY l.sku DESC
FOR JSON AUTO;
