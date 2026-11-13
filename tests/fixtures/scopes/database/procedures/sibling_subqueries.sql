-- Two sibling subqueries using the same alias, each its own table.
CREATE PROCEDURE lib.sibling_subqueries
AS
SET NOCOUNT ON;

SELECT m.member_id,
       (SELECT TOP (1) x.title FROM lib.book AS x WHERE x.donor_id = m.member_id ORDER BY x.book_id) AS donated,
       (SELECT TOP (1) x.loaned_on FROM lib.loan AS x WHERE x.member_id = m.member_id ORDER BY x.loan_id) AS first_loan
FROM lib.member AS m
ORDER BY m.member_id;
