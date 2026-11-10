-- The case that motivated slice 6d: a read whose CATCH calls the logger, which inserts. It is fully typed.
CREATE PROCEDURE depot.list_parts
    @code varchar(20) = NULL
AS
SET NOCOUNT ON;

BEGIN TRY
    SELECT p.part_id, p.code, p.name, p.note, s.qty, s.shelf
    FROM depot.part AS p
    LEFT JOIN depot.stock AS s ON s.part_id = p.part_id
    WHERE @code IS NULL OR p.code = @code
    ORDER BY p.part_id;
END TRY
BEGIN CATCH
    DECLARE @message nvarchar(4000) = ERROR_MESSAGE();
    EXEC depot.log_error N'depot.list_parts', @message;
    THROW;
END CATCH
