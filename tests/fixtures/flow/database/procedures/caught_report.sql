-- TRY … CATCH where CATCH returns a result after an error: an error at any point inside TRY reaches CATCH with the
-- results returned before it, so each position may hold either branch's rows, and the later ones may not run.
CREATE PROCEDURE flow.caught_report
    @fail_at tinyint = 0
AS
SET NOCOUNT ON;

BEGIN TRY
    IF @fail_at = 1 THROW 50002, N'failed before the sales', 1;
    SELECT s.sale_id, s.amount FROM flow.sale AS s ORDER BY s.sale_id;
    IF @fail_at = 2 THROW 50003, N'failed before the regions', 1;
    SELECT r.region_id, r.name FROM flow.region AS r ORDER BY r.region_id;
    IF @fail_at = 3 THROW 50004, N'failed at the end', 1;
END TRY
BEGIN CATCH
    SELECT p.part_id, p.name, p.unit FROM flow.part AS p ORDER BY p.part_id;
END CATCH
