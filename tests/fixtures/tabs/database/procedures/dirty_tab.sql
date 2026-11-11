-- Reads that may be uncommitted: a NOLOCK hint, then READ UNCOMMITTED for the rest of the procedure. A key no longer
-- bounds the rows, since such a scan can return a row twice.
CREATE PROCEDURE cafe.dirty_tab
    @tab_id int
AS
SET NOCOUNT ON;

SELECT t.tab_id, t.note
FROM cafe.tab AS t WITH (NOLOCK)
WHERE t.tab_id = @tab_id;

SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT b.tab_id, b.note
FROM cafe.tab_by_id(@tab_id) AS b;
