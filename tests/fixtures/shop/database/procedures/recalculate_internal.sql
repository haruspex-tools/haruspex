-- Not an interface: the config file excludes it.
CREATE PROCEDURE shop.recalculate_internal
AS
SET NOCOUNT ON;

SELECT count(*) AS lines FROM shop.basket_line;
