-- Three levels, each table inside the one selected before it: a customer's orders, each order's lines. placed_on comes after
-- a line's column and still goes in the order; the constant, after it, goes in the deepest object, the line.
CREATE PROCEDURE nest.customer_tree
AS
SET NOCOUNT ON;

SELECT c.name, o.order_id, l.sku, o.placed_on, c.customer_id, N'line' AS kind
FROM nest.customers AS c
JOIN nest.orders AS o ON o.customer_id = c.customer_id
JOIN nest.order_lines AS l ON l.order_id = o.order_id
ORDER BY c.customer_id, o.order_id, l.line_id
FOR JSON AUTO;
