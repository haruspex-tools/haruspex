-- A CTE of SELECT *, read by SELECT *, with each order's lines as a FOR JSON subquery of l.*.
CREATE PROCEDURE shop.orders_with_lines
AS
SET NOCOUNT ON;

WITH noted AS (SELECT * FROM shop.orders AS o WHERE o.note IS NOT NULL)
SELECT n.*,
       (SELECT l.* FROM shop.order_line AS l WHERE l.order_id = n.order_id ORDER BY l.line_id FOR JSON PATH) AS lines
FROM noted AS n
ORDER BY n.order_id;
