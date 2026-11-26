-- An updatable CTE: the UPDATE writes lib.loan through it, and OUTPUT reads what it wrote: lib.loan's columns, as declared,
-- under the CTE's names. A null date leaves the loans open, so returned_on may be null.
CREATE PROCEDURE lib.update_through_a_cte @member_id int, @returned_on date = '2026-11-04'
AS
SET NOCOUNT ON;

WITH open_loans AS (
    SELECT l.loan_id, l.returned_on
    FROM lib.loan AS l
    WHERE l.member_id = @member_id AND l.returned_on IS NULL
)
UPDATE open_loans
SET returned_on = @returned_on
OUTPUT inserted.loan_id, inserted.returned_on;
