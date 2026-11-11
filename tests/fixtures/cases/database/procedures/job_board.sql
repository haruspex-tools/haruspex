-- Every job as a row: labels from CASE, IIF and CHOOSE over literals (closed sets), branches that prove a value non-null,
-- NULLIF, and the reasons a CASE may be NULL (no ELSE, a NULL result, CHOOSE out of range).
CREATE PROCEDURE desk.job_board
AS
SET NOCOUNT ON;

SELECT j.job_id,
       CASE j.status WHEN 'N' THEN 'New' WHEN 'W' THEN 'In work' WHEN 'D' THEN 'Done' ELSE 'Cancelled' END AS status_label,
       CASE WHEN j.priority IS NULL THEN 'none' WHEN j.priority >= 3 THEN 'urgent' ELSE 'normal' END AS priority_label,
       CASE WHEN j.priority IS NULL THEN 0 ELSE j.priority END AS priority_value,
       CASE WHEN j.priority = 1 THEN 0 ELSE j.priority END AS priority_unless_one,
       IIF(j.quoted IS NULL, 0, j.quoted) AS quote_shown,
       CASE WHEN j.done_on > j.promised_on THEN 'late' END AS late,
       CASE WHEN j.status = 'X' THEN NULL ELSE j.job_id END AS live_job,
       CHOOSE(c.tier, 'bronze', 'silver', 'gold') AS tier_name,
       CHOOSE(j.priority, 'low', 'mid', 'high') AS priority_name,
       CHOOSE(c.tier, 'standard', 'priority') AS service,
       NULLIF(c.phone, '') AS phone,
       NULLIF(1, j.priority) AS one_unless_priority,
       CASE WHEN n.job_id IS NULL THEN N'no note' WHEN n.note IS NULL THEN N'blank' ELSE n.note END AS note,
       CASE WHEN n.job_id IS NOT NULL THEN n.job_id ELSE 0 END AS note_job
FROM desk.job AS j
JOIN desk.customer AS c ON c.customer_id = j.customer_id
LEFT JOIN desk.job_note AS n ON n.job_id = j.job_id
ORDER BY j.job_id;
