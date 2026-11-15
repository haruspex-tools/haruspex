-- A scalar function whose FOR JSON object is SELECT *: bound when the function runs.
CREATE FUNCTION shop.order_json (@order_id int)
RETURNS nvarchar(max)
AS
BEGIN
    RETURN (SELECT * FROM shop.orders AS o WHERE o.order_id = @order_id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
END;
