-- APPLY's right side reads its left input's columns: each member's latest loan, members without one dropped.
CREATE PROCEDURE lib.apply_reads_its_left_side
AS
SET NOCOUNT ON;

SELECT m.name, latest.title, latest.loaned_on
FROM lib.member AS m
CROSS APPLY (
    SELECT TOP (1) b.title, l.loaned_on
    FROM lib.loan AS l
    JOIN lib.book AS b ON b.book_id = l.book_id
    WHERE l.member_id = m.member_id
    ORDER BY l.loaned_on DESC, l.loan_id DESC
) AS latest
ORDER BY m.name;
