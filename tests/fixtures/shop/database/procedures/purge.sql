-- Writes, returns nothing; no include selects it.
CREATE PROCEDURE shop_admin.purge_baskets
AS
DELETE FROM shop.basket_line;
