-- VALUES lists: one row naming two columns, and the unpivot idiom: two int columns into one, null when either is, each
-- labelled by a string literal (a varchar of the longest label, never null).
CREATE PROCEDURE mart.product_sizes
AS
SET NOCOUNT ON;

SELECT pr.product_id,
       k.code,
       k.label,
       v.dimension,
       v.size_mm
FROM mart.product AS pr
CROSS APPLY (VALUES (pr.sku, pr.title)) AS k (code, label)
CROSS APPLY (VALUES ('width', pr.width_mm), ('height', pr.height_mm)) AS v (dimension, size_mm)
ORDER BY pr.product_id, v.dimension;
