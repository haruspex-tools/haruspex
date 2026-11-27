-- One plan, through the wrapper: the path is a literal and the plan's number, which the declaration's {id} stands for.
CREATE PROCEDURE billing.get_plan @plan_id int AS
DECLARE @path nvarchar(20) = N'/plans/' + CAST(ISNULL(@plan_id, 0) AS nvarchar(11));
EXEC billing.call_api @path = @path;
