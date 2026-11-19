-- One order's lines, counted, totalled and last shipped. Every column here is a surprise of its own: the count is an int that
-- may overflow (COUNT_BIG would not), the sum widens the price's type, and the last shipping date comes through an outer join
-- and an aggregate over possibly no rows. `haruspex explain shop.order_summary` lists them.
CREATE PROCEDURE shop.order_summary
    @order_id int
AS
SET NOCOUNT ON;

SELECT count(l.line_id) AS line_count,
       sum(l.price) AS total,
       max(l.shipped_at) AS last_shipped
FROM shop.customer_order AS o
LEFT JOIN shop.order_line AS l ON l.order_id = o.order_id
WHERE o.order_id = @order_id;
