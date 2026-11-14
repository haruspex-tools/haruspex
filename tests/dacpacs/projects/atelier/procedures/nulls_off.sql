-- With ANSI_NULLS off (the project sets it for this file), = NULL is true for a NULL.
CREATE PROCEDURE studio.nulls_off @peak int AS
SELECT k.kiln_id FROM studio.kiln AS k WHERE k.peak = @peak OR @peak = NULL;
