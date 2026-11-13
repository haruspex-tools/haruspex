-- A temporary table created in a called procedure: the callee's result set is the caller's, and the callee's #picks
-- does not outlive the call.
CREATE PROCEDURE lib.picks_from_a_call
AS
SET NOCOUNT ON;

EXEC lib.make_picks;
