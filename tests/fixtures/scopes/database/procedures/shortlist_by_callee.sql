-- The caller creates #shortlist, a procedure it calls fills it, and the caller reads it.
CREATE PROCEDURE lib.shortlist_by_callee
AS
SET NOCOUNT ON;

CREATE TABLE #shortlist (book_id int NOT NULL, title nvarchar(80) NOT NULL);
EXEC lib.fill_shortlist;

SELECT s.book_id, s.title
FROM #shortlist AS s
ORDER BY s.book_id;
