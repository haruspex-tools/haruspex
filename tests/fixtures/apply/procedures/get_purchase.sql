-- One purchase by key, at most one row: the CROSS APPLY of an ungrouped aggregate is one row for it, the OUTER APPLY of
-- TOP (1) at most one. The count is never null; the dearest price is null for a purchase without lines (an empty
-- aggregate), and so is the first title (OUTER APPLY found no row).
CREATE PROCEDURE mart.get_purchase
    @purchase_id int
AS
SET NOCOUNT ON;

SELECT p.purchase_id,
       p.status,
       s.line_count,
       s.top_price,
       f.first_title
FROM mart.purchase AS p
CROSS APPLY (
    SELECT COUNT_BIG(*) AS line_count, MAX(pl.unit_price) AS top_price
    FROM mart.purchase_line AS pl
    WHERE pl.purchase_id = p.purchase_id
) AS s
OUTER APPLY (
    SELECT TOP (1) pr.title AS first_title
    FROM mart.purchase_line AS pl
    JOIN mart.product AS pr ON pr.product_id = pl.product_id
    WHERE pl.purchase_id = p.purchase_id
    ORDER BY pl.line_no
) AS f
WHERE p.purchase_id = @purchase_id;
