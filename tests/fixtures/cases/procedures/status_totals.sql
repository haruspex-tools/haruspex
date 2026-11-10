-- Totals by status with a grand total (ROLLUP): CASE proves the rolled-up status and the aggregates non-null where it tests
-- them, and a parameter where it tests it.
CREATE PROCEDURE desk.status_totals
    @status char(1) = NULL
AS
SET NOCOUNT ON;

SELECT CASE WHEN j.status IS NULL THEN 'all' ELSE j.status END AS status,
       CASE WHEN @status IS NULL THEN 'any' ELSE @status END AS filter,
       COUNT_BIG(*) AS line_rows,
       CASE WHEN SUM(pl.price) IS NULL THEN 0 ELSE SUM(pl.price) END AS priced,
       CASE WHEN MAX(pl.qty) > 1 THEN MAX(pl.qty) END AS most
FROM desk.job AS j
LEFT JOIN desk.part_line AS pl ON pl.job_id = j.job_id
WHERE @status IS NULL OR j.status = @status
GROUP BY ROLLUP (j.status)
ORDER BY GROUPING(j.status), j.status;
