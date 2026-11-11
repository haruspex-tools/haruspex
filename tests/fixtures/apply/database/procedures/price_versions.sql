-- One product's prices with their history, five ways: a key filter does not make any of them at most one row.
CREATE PROCEDURE mart.price_versions
    @product_id int
AS
SET NOCOUNT ON;

SELECT p.amount FROM mart.price FOR SYSTEM_TIME ALL AS p WHERE p.product_id = @product_id;

SELECT a.amount FROM mart.price_all AS a WHERE a.product_id = @product_id;

SELECT n.amount FROM mart.price_now FOR SYSTEM_TIME ALL AS n WHERE n.product_id = @product_id;

-- A view of at most one row by its own key filter, read with its history, directly and applied to one product.
SELECT b.amount FROM mart.bowl_price FOR SYSTEM_TIME ALL AS b;

SELECT pr.product_id, x.amount
FROM mart.product AS pr
CROSS APPLY (SELECT b.amount FROM mart.bowl_price FOR SYSTEM_TIME ALL AS b) AS x
WHERE pr.product_id = @product_id;
