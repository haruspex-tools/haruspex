-- ROOT without a name, over PATH: the array under "root".
CREATE PROCEDURE nest.rooted_orders
AS
SET NOCOUNT ON;

SELECT o.order_id, o.note
FROM nest.orders AS o
ORDER BY o.order_id
FOR JSON PATH, ROOT;
