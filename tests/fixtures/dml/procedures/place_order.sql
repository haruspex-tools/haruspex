-- Places an order for one part: the new order from OUTPUT, the stock moved through OUTPUT … INTO a table variable (OUTPUT
-- without INTO fails on depot.stock, which has a trigger), then what was moved.
CREATE PROCEDURE depot.place_order
    @part_id int,
    @qty int,
    @note nvarchar(200) = NULL
AS
SET NOCOUNT ON;

DECLARE @placed table (order_id int NOT NULL);
DECLARE @moved table (part_id int NOT NULL, was int NOT NULL, now int NOT NULL);

BEGIN TRY
    BEGIN TRANSACTION;

    INSERT INTO depot.pick_order (placed_at, status)
    OUTPUT inserted.order_id INTO @placed (order_id)
    VALUES (SYSUTCDATETIME(), 'open');

    INSERT INTO depot.pick_order (placed_at, status, note)
    OUTPUT inserted.order_id, inserted.status, inserted.note
    VALUES (SYSUTCDATETIME(), 'open', @note);

    INSERT INTO depot.pick_line (order_id, part_id, qty)
    SELECT p.order_id, @part_id, @qty FROM @placed AS p;

    UPDATE depot.stock
    SET qty = qty - @qty
    OUTPUT inserted.part_id, deleted.qty, inserted.qty INTO @moved (part_id, was, now)
    WHERE part_id = @part_id;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    DECLARE @message nvarchar(4000) = ERROR_MESSAGE();
    EXEC depot.log_error N'depot.place_order', @message;
    THROW;
END CATCH

SELECT m.part_id, m.was, m.now FROM @moved AS m;
