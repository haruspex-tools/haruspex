-- The nested-payload idiom: each purchase with its lines as JSON, built per row by OUTER APPLY. The applied query has no
-- FROM, so it is one row for every purchase: lines is null only when the purchase has no lines (purchase 11), never because
-- the APPLY found nothing.
CREATE PROCEDURE mart.list_purchases
AS
SET NOCOUNT ON;

SELECT p.purchase_id,
       p.placed_on,
       l.lines
FROM mart.purchase AS p
OUTER APPLY (
    SELECT JSON_QUERY((
        SELECT pl.line_no, pl.quantity, pl.unit_price, pl.discount, pr.title
        FROM mart.purchase_line AS pl
        JOIN mart.product AS pr ON pr.product_id = pl.product_id
        WHERE pl.purchase_id = p.purchase_id
        ORDER BY pl.line_no
        FOR JSON PATH
    )) AS lines
) AS l
ORDER BY p.purchase_id;
