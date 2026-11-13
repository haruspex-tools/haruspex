-- ORDER BY may name a select-list alias, which no table has.
CREATE PROCEDURE lib.order_by_an_alias
AS
SET NOCOUNT ON;

SELECT b.title AS heading, b.pages
FROM lib.book AS b
ORDER BY heading;
