-- UPDATE with a FROM that joins a second table, OUTPUT reading the joined table.
CREATE PROCEDURE lib.update_returns @member_id int
AS
SET NOCOUNT ON;

UPDATE a
SET a.returned_on = '2026-11-03'
OUTPUT inserted.loan_id, deleted.returned_on AS was_returned, b.title
FROM lib.loan AS a
JOIN lib.book AS b ON b.book_id = a.book_id
WHERE a.member_id = @member_id;
