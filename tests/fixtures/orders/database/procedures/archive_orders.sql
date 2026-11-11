CREATE PROCEDURE sales.archive_orders
    @before datetime2(0)
AS
SET NOCOUNT ON;

IF @before IS NULL
    THROW 50000, N'@before is required', 1;

SELECT o.order_id
FROM sales.[order] AS o
WHERE o.placed_at < @before;
