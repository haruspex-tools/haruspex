-- An unqualified column that the subquery's own table lacks is the enclosing query's: lib.loan has no name, so name is
-- the member's.
CREATE PROCEDURE lib.unqualified_reaches_out
AS
SET NOCOUNT ON;

SELECT m.member_id,
       (SELECT TOP (1) name FROM lib.loan AS l WHERE l.member_id = m.member_id ORDER BY l.loan_id) AS borrower
FROM lib.member AS m
ORDER BY m.member_id;
