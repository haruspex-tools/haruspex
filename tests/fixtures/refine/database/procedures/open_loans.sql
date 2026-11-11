-- Loans still out, to members only: the WHERE on the member's side of the outer join drops the loans to other libraries,
-- so the member's columns are no longer null for want of a match; a nickname may still be null.
CREATE PROCEDURE lend.open_loans
AS
SET NOCOUNT ON;

SELECT l.loan_id, l.lent_on, l.due_on, m.member_id, m.name AS member, m.nickname
FROM lend.loan AS l
LEFT JOIN lend.member AS m ON m.member_id = l.member_id
WHERE l.returned_on IS NULL
  AND m.name IS NOT NULL
  AND l.due_on IS NOT NULL
ORDER BY l.loan_id;
