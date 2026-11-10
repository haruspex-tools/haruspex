-- The shared logger every CATCH block calls: it writes a row and returns nothing.
CREATE PROCEDURE depot.log_error
    @procedure sysname,
    @message nvarchar(4000)
AS
SET NOCOUNT ON;

INSERT INTO depot.error_log (procedure_name, message)
VALUES (@procedure, @message);
