-- Each product with its orders as JSON text in a rowset column, which only the shop module checks by parsing.
CREATE PROCEDURE shop.list_products
AS
SELECT p.product_id,
       (SELECT o.order_id FROM sales.orders AS o WHERE o.product_id = p.product_id ORDER BY o.order_id FOR JSON PATH) AS orders
FROM shop.products AS p;
