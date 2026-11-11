-- A callee whose only result set is on one branch.
CREATE PROCEDURE calls.maybe_ovens @show bit AS
SET NOCOUNT ON;
IF @show = 1
    SELECT o.oven_id, o.label FROM calls.oven AS o ORDER BY o.oven_id;
