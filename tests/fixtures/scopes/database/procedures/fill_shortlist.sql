-- Called by lib.shortlist_by_callee: fills the caller's #shortlist.
CREATE PROCEDURE lib.fill_shortlist
AS
SET NOCOUNT ON;

INSERT INTO #shortlist (book_id, title)
SELECT b.book_id, b.title FROM lib.book AS b WHERE b.pages IS NOT NULL;
