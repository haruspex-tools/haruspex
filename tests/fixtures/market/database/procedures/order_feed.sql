-- The orders as JSON: the status's value set holds as the column's collation compares, so its schema maps a value by the
-- collation's fold, which only the sales module needs.
CREATE PROCEDURE sales.order_feed
AS
SELECT o.order_id, o.status FROM sales.orders AS o ORDER BY o.order_id FOR JSON PATH;
