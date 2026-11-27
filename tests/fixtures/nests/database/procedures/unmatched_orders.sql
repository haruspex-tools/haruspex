-- Every line with its order on the outer side: a line whose order is gone is under an order object with its keys left out.
CREATE PROCEDURE nest.unmatched_orders
AS
SET NOCOUNT ON;

SELECT o.order_id, o.placed_on, l.sku
FROM nest.orders AS o
RIGHT JOIN nest.order_lines AS l ON l.order_id = o.order_id
ORDER BY l.line_id
FOR JSON AUTO;
