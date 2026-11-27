-- AUTO writes a dotted alias as one key, where PATH would nest it.
CREATE PROCEDURE nest.dotted_keys
AS
SET NOCOUNT ON;

SELECT o.order_id AS [order.id], l.sku AS [line.sku]
FROM nest.orders AS o
JOIN nest.order_lines AS l ON l.order_id = o.order_id
ORDER BY o.order_id, l.line_id
FOR JSON AUTO;
