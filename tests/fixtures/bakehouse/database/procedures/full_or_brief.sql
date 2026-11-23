-- bake.brief_or_full with its branches the other way round: the full variant comes first, so neither order may lose a key.
CREATE PROCEDURE bake.full_or_brief
    @full bit = 1
AS
SET NOCOUNT ON;

IF @full = 1
    SELECT h.shop_id AS id,
        (SELECT s.sale_id, s.loaves FROM bake.sale AS s WHERE s.shop_id = h.shop_id ORDER BY s.sale_id FOR JSON PATH) AS sales
    FROM bake.shop AS h ORDER BY h.shop_id FOR JSON PATH;
ELSE
    SELECT h.shop_id AS id,
        (SELECT s.sale_id FROM bake.sale AS s WHERE s.shop_id = h.shop_id ORDER BY s.sale_id FOR JSON PATH) AS sales
    FROM bake.shop AS h ORDER BY h.shop_id FOR JSON PATH;
