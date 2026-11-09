-- A basket with its customer and lines. The customer comes through a left join, so inference says its name may be
-- null; the config file declares it never is.
CREATE PROCEDURE shop.get_basket
    @basket_id int
AS
SET NOCOUNT ON;

SELECT b.basket_id AS id,
       c.name AS customerName,
       (
           SELECT l.line_no AS position,
                  p.sku,
                  l.quantity
           FROM shop.basket_line AS l
           JOIN shop.product AS p ON p.product_id = l.product_id
           WHERE l.basket_id = b.basket_id
           ORDER BY l.line_no
           FOR JSON PATH
       ) AS lines
FROM shop.basket AS b
LEFT JOIN shop.customer AS c ON c.customer_id = b.customer_id
WHERE b.basket_id = @basket_id
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
