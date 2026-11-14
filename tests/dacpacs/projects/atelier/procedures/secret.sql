CREATE PROCEDURE studio.secret WITH ENCRYPTION AS
SELECT k.kiln_id FROM studio.kiln AS k;
