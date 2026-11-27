CREATE VIEW nest.order_dates
AS
SELECT o.order_id, o.placed_on
FROM nest.orders AS o;
GO
