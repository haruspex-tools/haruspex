CREATE PROCEDURE depot.recent_note
AS
SET NOCOUNT ON;

SELECT CAST(N'depot.purge' AS nvarchar(128)) AS procedure_name, p.note AS message FROM depot.part AS p WHERE p.note IS NOT NULL;
