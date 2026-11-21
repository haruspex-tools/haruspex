-- The budget view as a rowset: every division typed.
CREATE PROCEDURE store.budget_report
AS
SET NOCOUNT ON;

SELECT b.product_id, b.per_day, b.per_call, b.per_month, b.per_credit
FROM store.budget AS b
ORDER BY b.product_id;
