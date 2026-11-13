-- A temporary table created in one branch and read after it. SQL Server resolves #picked when the SELECT runs, so a call
-- that skips the branch fails there; the runs take it.
CREATE PROCEDURE lib.temp_table_from_a_branch @pages smallint
AS
SET NOCOUNT ON;

IF @pages IS NOT NULL
BEGIN
    CREATE TABLE #picked (book_id int NOT NULL, title nvarchar(80) NOT NULL);
    INSERT INTO #picked (book_id, title)
    SELECT b.book_id, b.title FROM lib.book AS b WHERE b.pages >= @pages;
END

SELECT p.book_id, p.title
FROM #picked AS p
ORDER BY p.book_id;
