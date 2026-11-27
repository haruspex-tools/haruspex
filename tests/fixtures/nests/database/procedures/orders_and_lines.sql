-- Every order, with its lines or, for one with none, one line object of no keys. A note is left out when null, and the
-- doubled quantity, a column of no table after a line's column, is written in the line.
CREATE PROCEDURE nest.orders_and_lines
AS
SET NOCOUNT ON;

SELECT o.order_id, o.note, l.sku, l.qty * 2 AS double_qty
FROM nest.orders AS o
LEFT JOIN nest.order_lines AS l ON l.order_id = o.order_id
ORDER BY o.order_id, l.line_id
FOR JSON AUTO;
