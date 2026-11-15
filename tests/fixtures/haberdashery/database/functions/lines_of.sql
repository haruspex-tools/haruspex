-- An inline function whose query is SELECT *: bound when the function is created, as a view's is (decision 0047).
CREATE FUNCTION shop.lines_of (@order_id int)
RETURNS TABLE
AS
RETURN SELECT * FROM shop.order_line AS l WHERE l.order_id = @order_id;
