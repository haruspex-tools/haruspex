CREATE PROCEDURE dye.list_vats
AS
SELECT v.vat_id, v.state, v.shade, v.mode, v.batch, v.label, v.grade, v.tone, v.finish
FROM dye.vat AS v
ORDER BY v.vat_id;
GO
