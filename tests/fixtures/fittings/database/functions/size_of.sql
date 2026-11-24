-- A scalar function that reads a masked column returns its mask, through a variable or not.
CREATE FUNCTION tailor.size_of (@customer_id int)
RETURNS char(3)
AS
BEGIN
    DECLARE @size char(3) = (SELECT size_code FROM tailor.customer WHERE customer_id = @customer_id);
    RETURN @size;
END;
