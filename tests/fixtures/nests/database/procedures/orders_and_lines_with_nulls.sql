-- orders_and_lines with INCLUDE_NULL_VALUES: an order with no line has one line object whose keys are null.
CREATE PROCEDURE nest.orders_and_lines_with_nulls
AS
SET NOCOUNT ON;

SELECT o.order_id, o.note, l.sku, l.qty * 2 AS double_qty
FROM nest.orders AS o
LEFT JOIN nest.order_lines AS l ON l.order_id = o.order_id
ORDER BY o.order_id, l.line_id
FOR JSON AUTO, INCLUDE_NULL_VALUES;
