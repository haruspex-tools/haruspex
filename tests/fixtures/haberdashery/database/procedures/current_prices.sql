-- A temporal table's star, without its hidden period columns.
CREATE PROCEDURE shop.current_prices
AS
SET NOCOUNT ON;

SELECT * FROM shop.price ORDER BY sku;
