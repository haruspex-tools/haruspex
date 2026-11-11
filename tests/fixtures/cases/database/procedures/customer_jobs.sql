-- Each customer's jobs as a rowset column of JSON text: ISNULL never null, COALESCE null only on its second read.
CREATE PROCEDURE desk.customer_jobs
AS
SET NOCOUNT ON;

SELECT c.customer_id,
       ISNULL((SELECT j.job_id AS id, j.status FROM desk.job AS j WHERE j.customer_id = c.customer_id ORDER BY j.job_id FOR JSON PATH), '[]') AS jobs,
       COALESCE((SELECT j.job_id AS id FROM desk.job AS j WHERE j.customer_id = c.customer_id ORDER BY j.job_id FOR JSON PATH), '[]') AS job_ids
FROM desk.customer AS c
ORDER BY c.customer_id;
