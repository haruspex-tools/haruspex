-- The copies at one branch: a filter on part of the copy's two-column key, so any number of rows.
CREATE PROCEDURE lend.copies_at
    @branch_id int
AS
SET NOCOUNT ON;

SELECT c.copy_no, c.title_id, c.shelf
FROM lend.copy AS c
WHERE c.branch_id = @branch_id
ORDER BY c.copy_no;
