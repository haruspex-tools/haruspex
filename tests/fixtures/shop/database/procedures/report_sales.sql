-- In another schema, whose block gives it a prefix and its own replacement: AdminSales.
CREATE PROCEDURE shop_admin.report_sales
AS
SET NOCOUNT ON;

SELECT p.sku,
       p.price
FROM shop.product AS p
ORDER BY p.sku
FOR JSON PATH;
