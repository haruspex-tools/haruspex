-- Two result sets, which the config file names: the products, as report_sales writes them, then how many there are. Its
-- function returns { products, count }, and its products' rows share report_sales's shape, which the config file names.
CREATE PROCEDURE shop_admin.report_stock
AS
SET NOCOUNT ON;

SELECT p.sku,
       p.price
FROM shop.product AS p
ORDER BY p.sku
FOR JSON PATH;

SELECT COUNT_BIG(*) AS products
FROM shop.product AS p;
