-- History fixture for SQL, which Vale doesn't read: check.sh applies haruspex.History's tokens to every .sql file.
-- Each comment below breaks the rule once; the last lines pass.

-- slice 6g added this procedure.
/* fixed in review round 1 */
-- read since 6h.
CREATE PROCEDURE dbo.history AS
    SELECT 2e+076 AS f, 'a slice of bread' AS s; -- a slice of data passes
