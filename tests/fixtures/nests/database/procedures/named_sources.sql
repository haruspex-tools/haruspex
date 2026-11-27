-- Sources without an alias are keyed by their names as the FROM writes them: a table, a view and a function, each nested in
-- the one before. The view's column is the order's date, so it is never null.
CREATE PROCEDURE nest.named_sources
AS
SET NOCOUNT ON;

SELECT o.order_id, nest.order_dates.placed_on, lines_of.sku
FROM nest.orders AS o
JOIN nest.order_dates ON order_dates.order_id = o.order_id
CROSS APPLY nest.lines_of(o.order_id)
ORDER BY o.order_id
FOR JSON AUTO;
