-- One order as JSON, its lines nested, with the logger in CATCH.
CREATE PROCEDURE depot.get_order
    @order_id int
AS
SET NOCOUNT ON;

BEGIN TRY
    SELECT o.order_id AS id,
           o.status,
           o.note,
           (SELECT l.part_id AS part, l.qty
            FROM depot.pick_line AS l
            WHERE l.order_id = o.order_id
            FOR JSON PATH) AS lines
    FROM depot.pick_order AS o
    WHERE o.order_id = @order_id
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
END TRY
BEGIN CATCH
    DECLARE @message nvarchar(4000) = ERROR_MESSAGE();
    EXEC depot.log_error N'depot.get_order', @message;
    THROW;
END CATCH
