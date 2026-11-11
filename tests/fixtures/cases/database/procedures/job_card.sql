-- One job as a JSON object, its part lines nested in each way the common idiom is written: COALESCE (escaped text, and read
-- twice), ISNULL (JSON, never null), JSON_QUERY over COALESCE (JSON again), and the first line WITHOUT_ARRAY_WRAPPER with an
-- empty object as its fallback.
CREATE PROCEDURE desk.job_card
    @job_id int
AS
SET NOCOUNT ON;

SELECT j.job_id AS id,
       CASE j.status WHEN 'N' THEN 'New' WHEN 'W' THEN 'In work' WHEN 'D' THEN 'Done' ELSE 'Cancelled' END AS status,
       COALESCE((SELECT pl.part, pl.qty FROM desk.part_line AS pl WHERE pl.job_id = j.job_id ORDER BY pl.line_id FOR JSON PATH), '[]') AS lines_text,
       ISNULL((SELECT pl.part,
                      pl.qty,
                      CASE WHEN pl.price IS NULL THEN 0 ELSE pl.qty * pl.price END AS line_total,
                      IIF(pl.price IS NULL OR pl.price = 0, 'yes', 'no') AS free
               FROM desk.part_line AS pl
               WHERE pl.job_id = j.job_id
               ORDER BY pl.line_id
               FOR JSON PATH), '[]') AS lines,
       JSON_QUERY(COALESCE((SELECT pl.line_id AS id FROM desk.part_line AS pl WHERE pl.job_id = j.job_id ORDER BY pl.line_id FOR JSON PATH), '[]')) AS line_ids,
       ISNULL((SELECT pl.part FROM desk.part_line AS pl WHERE pl.job_id = j.job_id ORDER BY pl.line_id FOR JSON PATH), '{}') AS parts_or_none,
       ISNULL((SELECT TOP (1) pl.part FROM desk.part_line AS pl WHERE pl.job_id = j.job_id ORDER BY pl.line_id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), '{}') AS first_text,
       JSON_QUERY(ISNULL((SELECT TOP (1) pl.part FROM desk.part_line AS pl WHERE pl.job_id = j.job_id ORDER BY pl.line_id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), '{}')) AS first_line
FROM desk.job AS j
WHERE j.job_id = @job_id
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
