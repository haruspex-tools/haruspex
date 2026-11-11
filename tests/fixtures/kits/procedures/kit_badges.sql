-- A variable assigned on several paths and in a loop, JSON literals, a stored JSON column, which is never traced, and a variable
-- holding JSON on one path and other text on another.
CREATE PROCEDURE kit.kit_badges @kit_id int
AS
SET NOCOUNT ON;
DECLARE @badge nvarchar(max), @n int = 0, @tally nvarchar(max) = '[]', @stored nvarchar(max), @note nvarchar(max) = json_object('kit': @kit_id);

IF EXISTS (SELECT 1 FROM kit.kit_part AS p WHERE p.kit_id = @kit_id)
    SET @badge = json_object('kind': N'parts', 'count': (SELECT COUNT_BIG(*) FROM kit.kit_part AS p WHERE p.kit_id = @kit_id));
ELSE
    SET @badge = '{"kind": "empty", "count": 0}';

WHILE @n < 2
BEGIN
    SET @tally = (SELECT TOP (@n) p.sku FROM kit.kit_part AS p ORDER BY p.part_id FOR JSON PATH);
    SET @n += 1;
END

SELECT @stored = k.extras FROM kit.kit AS k WHERE k.kit_id = @kit_id;

-- Text that is not JSON on one path: the variable is a plain string, never JSON text.
IF @stored IS NULL SET @note = N'none';

SELECT json_query(@badge) AS badge,
       json_query(ISNULL(@tally, '[]')) AS tally,
       json_query('[{"tier": "gold"}, {"tier": "silver", "since": 2024}]') AS tiers,
       json_query(@stored) AS stored,
       @note AS note
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
