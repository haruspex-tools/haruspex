-- One order's object without the array: its rows are adjacent, so AUTO merges them into one object with every line.
CREATE PROCEDURE nest.one_order @order_id int
AS
SET NOCOUNT ON;

SELECT o.order_id, o.placed_on, l.sku
FROM nest.orders AS o
JOIN nest.order_lines AS l ON l.order_id = o.order_id
WHERE o.order_id = @order_id
ORDER BY l.line_id
FOR JSON AUTO, WITHOUT_ARRAY_WRAPPER;
