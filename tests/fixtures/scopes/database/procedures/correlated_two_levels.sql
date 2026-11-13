-- A correlated subquery whose own subquery reaches two levels out, to the member of the outermost query.
CREATE PROCEDURE lib.correlated_two_levels
AS
SET NOCOUNT ON;

SELECT m.member_id,
       (
           SELECT TOP (1) b.title
           FROM lib.book AS b
           WHERE b.donor_id = m.member_id
             AND EXISTS (SELECT 1 FROM lib.loan AS l WHERE l.book_id = b.book_id AND l.member_id = m.member_id)
           ORDER BY b.book_id
       ) AS own_book_borrowed
FROM lib.member AS m
ORDER BY m.member_id;
