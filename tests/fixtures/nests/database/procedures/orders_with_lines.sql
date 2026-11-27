-- Each order with an array of its lines, under the lines' alias: an order with no line has no row, so it isn't there.
CREATE PROCEDURE nest.orders_with_lines
AS
SET NOCOUNT ON;

SELECT o.order_id, o.placed_on, l.sku, l.qty
FROM nest.orders AS o
JOIN nest.order_lines AS l ON l.order_id = o.order_id
ORDER BY o.order_id, l.line_id
FOR JSON AUTO;
