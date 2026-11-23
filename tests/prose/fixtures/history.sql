-- History fixture for the files Vale doesn't read: check.sh applies haruspex.History's tokens to every .sql file, and to
-- the scripts and project files. Each comment below breaks the rule once; the last lines pass.

-- slice 6g added this procedure.
/* fixed in review round 1 */
-- read since 6h.
-- typed (K1c) here, as the brief said.
CREATE PROCEDURE dbo.history AS
    SELECT 2e+076 AS f, 'a slice of bread' AS s; -- a slice of data passes
SELECT 1 AS n FROM (SELECT 1 AS w) AS w1; -- an alias (w1) in lower case passes, and so does row 3 of the table
