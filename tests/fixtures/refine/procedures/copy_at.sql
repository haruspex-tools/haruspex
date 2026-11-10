-- A copy by its two-column key, with its title joined by the title's key: still at most one row.
CREATE PROCEDURE lend.copy_at
    @branch_id int,
    @copy_no int
AS
SET NOCOUNT ON;

SELECT c.branch_id, c.copy_no, c.shelf, t.name AS title, t.author
FROM lend.copy AS c
JOIN lend.title AS t ON t.title_id = c.title_id
WHERE c.branch_id = @branch_id AND c.copy_no = @copy_no;
