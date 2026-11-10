-- VALUES lists: one row naming two columns, and an unpivot of two int columns into one, null when either is.
CREATE PROCEDURE mart.product_sizes
AS
SET NOCOUNT ON;

SELECT pr.product_id,
       k.code,
       k.label,
       v.size_mm
FROM mart.product AS pr
CROSS APPLY (VALUES (pr.sku, pr.title)) AS k (code, label)
CROSS APPLY (VALUES (pr.width_mm), (pr.height_mm)) AS v (size_mm)
ORDER BY pr.product_id;
