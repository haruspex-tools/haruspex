-- Branches: a guard that throws, three branches returning different rows at the same position (one a superset of
-- another's columns, which the Zod must not read as the subset), and a branch that returns early, so neither result
-- need arrive.
CREATE PROCEDURE flow.sales_by_mode
    @mode tinyint,
    @region int = NULL
AS
SET NOCOUNT ON;

IF @mode IS NULL
    THROW 50001, N'@mode is required', 1;

IF @mode = 1
    SELECT s.sale_id, s.amount FROM flow.sale AS s ORDER BY s.sale_id;
ELSE IF @mode = 2
    SELECT r.region_id, r.name FROM flow.region AS r ORDER BY r.region_id;
ELSE IF @mode = 4
    SELECT r.region_id, r.name, r.code FROM flow.region AS r ORDER BY r.region_id;
ELSE
    RETURN;

SELECT r.code, r.name FROM flow.region AS r WHERE r.region_id = @region FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
