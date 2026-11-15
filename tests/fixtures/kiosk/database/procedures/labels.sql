-- A key named by a variable: the result's key is the variable's value, so the result's keys are unknown. A project's build
-- refuses a variable in an object's name (SQL70604), so a column can't be named by one; an alias can.
CREATE PROCEDURE kiosk.labels
AS
SELECT s.stall_id, s.kind AS [$(Label)] FROM kiosk.stall AS s;
GO
