-- The product and the orders of it: its second set's rows are the shape the sales procedures return.
CREATE PROCEDURE shop.get_product @product_id int
AS
SELECT p.product_id, p.name, p.price FROM shop.products AS p WHERE p.product_id = @product_id;
SELECT o.order_id, o.total FROM sales.orders AS o WHERE o.product_id = @product_id;
