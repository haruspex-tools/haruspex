-- Every order, every column, in declared order.
CREATE PROCEDURE shop.list_orders
AS
SET NOCOUNT ON;

SELECT * FROM shop.orders ORDER BY order_id;
