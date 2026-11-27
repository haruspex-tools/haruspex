-- Every plan, through the wrapper, its method left to the wrapper's default.
CREATE PROCEDURE billing.list_plans AS
EXEC billing.call_api N'/plans';
