-- DELETE through a join, OUTPUT reading deleted and the joined table.
CREATE PROCEDURE lib.delete_returns @member_id int
AS
SET NOCOUNT ON;

DELETE a
OUTPUT deleted.loan_id, deleted.returned_on, b.pages
FROM lib.loan AS a
JOIN lib.book AS b ON b.book_id = a.book_id
WHERE a.member_id = @member_id;
