-- Arithmetic and string building on the menu: a label built with +, a price with tax (numeric, since 1.2 is), pence cast to
-- int (which may overflow), a code with a fallback, a reference that CONCAT never makes null, and a title read as a number
-- (TRY_CAST of a column never null: null for every title but the house blend's, 1920).
CREATE PROCEDURE cafe.menu_prices
AS
SET NOCOUNT ON;

SELECT mi.item_id,
       mi.title + N' (' + CONVERT(nvarchar(12), mi.price) + N')' AS label,
       mi.price * 1.2 AS price_with_tax,
       CAST(mi.price * 100 AS int) AS pence,
       ISNULL(mi.short_code, '----') AS code,
       CONCAT(mi.short_code, ':', mi.item_id) AS ref,
       TRY_CAST(mi.title AS int) AS title_number
FROM cafe.menu_item AS mi
ORDER BY mi.item_id;
