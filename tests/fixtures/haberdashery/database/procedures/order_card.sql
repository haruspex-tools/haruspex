-- One order as an object with a key per column of shop.orders; note is left out when it is null.
CREATE PROCEDURE shop.order_card @id int
AS
SET NOCOUNT ON;

SELECT * FROM shop.orders WHERE order_id = @id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
