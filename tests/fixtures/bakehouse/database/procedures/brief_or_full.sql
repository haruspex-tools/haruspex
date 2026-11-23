-- Two branches returning JSON objects of the same keys, whose nested sales differ: the brief branch's have only an id, the full
-- branch's the loaves as well. The Zod must keep a full object's loaves, though the brief variant comes first and its objects
-- have the same keys. bake.full_or_brief has the branches the other way round.
CREATE PROCEDURE bake.brief_or_full
    @full bit = 0
AS
SET NOCOUNT ON;

IF @full = 0
    SELECT h.shop_id AS id,
        (SELECT s.sale_id FROM bake.sale AS s WHERE s.shop_id = h.shop_id ORDER BY s.sale_id FOR JSON PATH) AS sales
    FROM bake.shop AS h ORDER BY h.shop_id FOR JSON PATH;
ELSE
    SELECT h.shop_id AS id,
        (SELECT s.sale_id, s.loaves FROM bake.sale AS s WHERE s.shop_id = h.shop_id ORDER BY s.sale_id FOR JSON PATH) AS sales
    FROM bake.shop AS h ORDER BY h.shop_id FOR JSON PATH;
