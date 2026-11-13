-- Unqualified columns resolved by the nearest query that has them: email is the member's, two levels out; title is the
-- inner book's, though the outer query reads a book too, so it does not go outward.
CREATE PROCEDURE lib.unqualified_two_levels_out
AS
SET NOCOUNT ON;

SELECT m.member_id,
       (
           SELECT TOP (1) (SELECT TOP (1) email FROM lib.book AS b2 WHERE b2.book_id = l.book_id)
           FROM lib.loan AS l
           WHERE l.member_id = m.member_id
           ORDER BY l.loan_id
       ) AS borrower_email,
       (SELECT TOP (1) title FROM lib.book AS b3 WHERE b3.donor_id = m.member_id ORDER BY b3.book_id) AS donated,
       ob.title AS any_title
FROM lib.member AS m
CROSS JOIN (SELECT TOP (1) b.title FROM lib.book AS b ORDER BY b.book_id) AS ob
ORDER BY m.member_id;
