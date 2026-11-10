-- One row for every tab, even one without items: an ungrouped aggregate. The subtotal is never null (COALESCE with 0), a
-- decimal(38,2) that cannot overflow (SUM of a decimal(15,2)); the discounts are null for a tab without discounts.
CREATE FUNCTION cafe.tab_totals (@tab_id int)
RETURNS TABLE
AS
RETURN
SELECT COUNT_BIG(*) AS items,
       COALESCE(SUM(ti.qty * mi.price), 0) AS subtotal,
       SUM(ti.discount) AS discounts
FROM cafe.tab_item AS ti
JOIN cafe.menu_item AS mi ON mi.item_id = ti.item_id
WHERE ti.tab_id = @tab_id;
