CREATE PROCEDURE sales.list_orders @placed date
AS
SELECT o.order_id, o.total FROM sales.orders AS o WHERE o.placed = @placed;
