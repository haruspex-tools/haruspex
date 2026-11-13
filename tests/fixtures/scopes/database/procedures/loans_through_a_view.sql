-- A view's column that comes from a derived table inside it, outer-joined so a member without loans has none.
CREATE PROCEDURE lib.loans_through_a_view
AS
SET NOCOUNT ON;

SELECT m.member_id, v.loans
FROM lib.member AS m
LEFT JOIN lib.member_loans AS v ON v.member_id = m.member_id
ORDER BY m.member_id;
