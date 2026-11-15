-- An order's customer, then every column of its lines: alias.* on the optional side of a left join.
CREATE PROCEDURE shop.order_lines @id int
AS
SET NOCOUNT ON;

SELECT o.customer, l.*
FROM shop.orders AS o
LEFT JOIN shop.order_line AS l ON l.order_id = o.order_id
WHERE o.order_id = @id
ORDER BY l.line_id;
