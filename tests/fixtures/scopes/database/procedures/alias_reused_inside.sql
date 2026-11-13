-- A derived table's alias reused inside its own query: the inner d is lib.book, whose WHERE proves pages not null, and
-- the outer d is the derived table, which keeps that proof.
CREATE PROCEDURE lib.alias_reused_inside
AS
SET NOCOUNT ON;

SELECT d.title, d.pages
FROM (
    SELECT d.title, d.pages
    FROM lib.book AS d
    WHERE d.pages IS NOT NULL
) AS d
ORDER BY d.title;
