-- The member's latest loan: TOP (1) gives at most one row, and none for a member who never borrowed.
CREATE PROCEDURE lend.latest_loan
    @member_id int
AS
SET NOCOUNT ON;

SELECT TOP (1) l.loan_id, l.lent_on, l.returned_on, l.fine
FROM lend.loan AS l
WHERE l.member_id = @member_id
ORDER BY l.lent_on DESC, l.loan_id DESC;
