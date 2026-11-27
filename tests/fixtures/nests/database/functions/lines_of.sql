CREATE FUNCTION nest.lines_of (@order_id int)
RETURNS TABLE
AS
RETURN SELECT l.sku, l.qty
       FROM nest.order_lines AS l
       WHERE l.order_id = @order_id;
GO
