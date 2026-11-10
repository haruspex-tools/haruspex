-- A view whose query is SELECT *.
CREATE PROCEDURE mart.list_products
AS
SET NOCOUNT ON;

SELECT pa.product_id,
       pa.sku,
       pa.title,
       pa.colour,
       pa.width_mm
FROM mart.product_all AS pa
ORDER BY pa.product_id;
