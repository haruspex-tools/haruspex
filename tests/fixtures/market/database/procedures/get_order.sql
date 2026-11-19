CREATE PROCEDURE sales.get_order @order_id int
AS
SELECT o.order_id, o.total FROM sales.orders AS o WHERE o.order_id = @order_id;
