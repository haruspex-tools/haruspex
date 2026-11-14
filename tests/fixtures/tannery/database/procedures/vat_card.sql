CREATE PROCEDURE dye.vat_card @vat_id int
AS
SELECT v.vat_id AS id, v.state, v.shade, v.mode
FROM dye.vat AS v
WHERE v.vat_id = @vat_id
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
GO
