-- A call declared to return no result set (WITH RESULT SETS NONE): maybe_ovens returns none when not shown, and a call
-- that does return one would fail. So only this procedure's own set arrives.
CREATE PROCEDURE calls.quiet_then_ovens AS
SET NOCOUNT ON;
EXEC calls.maybe_ovens @show = 0 WITH RESULT SETS NONE;
SELECT o.oven_id FROM calls.oven AS o ORDER BY o.oven_id;
