-- ROOT with a name, over AUTO.
CREATE PROCEDURE nest.rooted_tree
AS
SET NOCOUNT ON;

SELECT o.order_id, l.sku
FROM nest.orders AS o
JOIN nest.order_lines AS l ON l.order_id = o.order_id
ORDER BY o.order_id, l.line_id
FOR JSON AUTO, ROOT(N'orders');
