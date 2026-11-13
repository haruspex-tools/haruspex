-- MERGE from a query, OUTPUT reading $action, inserted, deleted and the source.
CREATE PROCEDURE lib.merge_returns @member_id int
AS
SET NOCOUNT ON;

MERGE lib.loan AS t
USING (
    SELECT b.book_id, b.title
    FROM lib.book AS b
    WHERE b.donor_id = @member_id
) AS s
ON t.book_id = s.book_id AND t.member_id = @member_id
WHEN MATCHED THEN
    UPDATE SET t.returned_on = '2026-11-02'
WHEN NOT MATCHED BY TARGET THEN
    INSERT (member_id, book_id, loaned_on) VALUES (@member_id, s.book_id, '2026-11-02')
OUTPUT $action AS did, inserted.loan_id, deleted.returned_on AS was_returned, s.title;
