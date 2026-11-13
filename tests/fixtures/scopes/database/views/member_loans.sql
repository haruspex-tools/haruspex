-- A view over a derived table: the view's columns are the derived table's, which are its query's.
CREATE VIEW lib.member_loans
AS
SELECT d.member_id, d.loans
FROM (
    SELECT l.member_id, COUNT_BIG(*) AS loans
    FROM lib.loan AS l
    GROUP BY l.member_id
) AS d;
