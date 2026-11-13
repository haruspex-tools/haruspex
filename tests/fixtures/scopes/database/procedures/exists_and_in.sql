-- Correlated EXISTS and IN subqueries filtering the outer rows.
CREATE PROCEDURE lib.exists_and_in
AS
SET NOCOUNT ON;

SELECT m.member_id, m.email
FROM lib.member AS m
WHERE EXISTS (SELECT 1 FROM lib.loan AS l WHERE l.member_id = m.member_id AND l.returned_on IS NULL)
   OR m.member_id IN (SELECT b.donor_id FROM lib.book AS b WHERE b.pages > m.member_id)
ORDER BY m.member_id;
