-- Creates #picks and returns it; the table is gone when the procedure returns.
CREATE PROCEDURE lib.make_picks
AS
SET NOCOUNT ON;

CREATE TABLE #picks (book_id int NOT NULL, title nvarchar(80) NULL);
INSERT INTO #picks (book_id, title)
SELECT b.book_id, b.title FROM lib.book AS b;
INSERT INTO #picks (book_id, title) VALUES (0, NULL);

SELECT p.book_id, p.title FROM #picks AS p ORDER BY p.book_id;
