-- Products as rows: a value set the config file declares exact, and JSON whose shape the application defines.
CREATE PROCEDURE shop.list_products
AS
SET NOCOUNT ON;

SELECT p.product_id,
       p.sku,
       p.name,
       p.status,
       p.attributes,
       p.price
FROM shop.product AS p
ORDER BY p.product_id;
