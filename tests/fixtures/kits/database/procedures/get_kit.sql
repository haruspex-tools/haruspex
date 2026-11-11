-- JSON built into variables: a FOR JSON subquery, then a json_object assigned by a SELECT that may match no rows, read back
-- through json_query, with a literal fallback.
CREATE PROCEDURE kit.get_kit @kit_id int
AS
SET NOCOUNT ON;
DECLARE @parts nvarchar(max) = (SELECT p.sku, p.qty, p.colour FROM kit.kit_part AS p WHERE p.kit_id = @kit_id ORDER BY p.part_id FOR JSON PATH);
DECLARE @summary nvarchar(max);

SELECT @summary = json_object('name': k.name, 'price': k.price, 'parts': json_query(@parts))
FROM kit.kit AS k
WHERE k.kit_id = @kit_id;

SELECT json_query(@summary) AS kit,
       json_query(COALESCE(@parts, '[]')) AS parts,
       json_query(CASE WHEN @kit_id = 2 THEN '{"empty": true}' ELSE @summary END) AS either
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
