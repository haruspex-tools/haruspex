-- One member by key, as one JSON object, with counts and a date from scalar subqueries and the home branch from an outer
-- join. COUNT_BIG over no loans is 0; COUNT is too, but overflows to NULL under a caller's ANSI_WARNINGS off; MAX is NULL.
CREATE PROCEDURE lend.get_member
    @member_id int
AS
SET NOCOUNT ON;

SELECT m.member_id AS id,
       m.name,
       m.nickname,
       m.email,
       (SELECT COUNT_BIG(*) FROM lend.loan AS l WHERE l.member_id = m.member_id) AS loans,
       (SELECT COUNT(*) FROM lend.loan AS l WHERE l.member_id = m.member_id AND l.returned_on IS NULL) AS open_loans,
       (SELECT MAX(l.lent_on) FROM lend.loan AS l WHERE l.member_id = m.member_id) AS last_loan,
       b.name AS [home.name],
       b.city AS [home.city]
FROM lend.member AS m
LEFT JOIN lend.branch AS b ON b.branch_id = m.home_branch_id
WHERE m.member_id = @member_id
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
