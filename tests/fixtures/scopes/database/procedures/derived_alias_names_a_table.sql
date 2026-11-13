-- A derived table whose alias is a schema table's name: loan here is the derived table, not lib.loan or a table named
-- loan in dbo.
CREATE PROCEDURE lib.derived_alias_names_a_table
AS
SET NOCOUNT ON;

SELECT loan.member_id, loan.last_on
FROM (
    SELECT l.member_id, MAX(l.loaned_on) AS last_on
    FROM lib.loan AS l
    GROUP BY l.member_id
) AS loan
ORDER BY loan.member_id;
