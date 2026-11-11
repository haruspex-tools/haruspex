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

-- Assignments that may not happen keep a value of another shape: a SELECT over no rows, and a SET that fails inside TRY.
DECLARE @latest nvarchar(max) = N'{"none": true}', @share nvarchar(max) = N'[]';
SELECT @latest = json_object('sku': p.sku) FROM kit.kit_part AS p WHERE p.kit_id = @kit_id ORDER BY p.part_id;
BEGIN TRY
    SET @share = json_object('per_part': (SELECT 100 / COUNT(*) FROM kit.kit_part AS p WHERE p.kit_id = @kit_id));
END TRY
BEGIN CATCH
END CATCH

-- Text that is not JSON on one path: the variable is a plain string, never JSON text.
IF @stored IS NULL SET @note = N'none';

SELECT json_query(@badge) AS badge,
       json_query(ISNULL(@tally, '[]')) AS tally,
       json_query('[{"tier": "gold"}, {"tier": "silver", "since": 2024}]') AS tiers,
       json_query(@stored) AS stored,
       @note AS note,
       json_query(@latest) AS latest,
       json_query(@share) AS share,
       -- Without N the literal is read in the database's code page, which may change ā: not read here.
       json_query('["ā"]') AS accent,
       json_query(N'["ā"]') AS accent_n
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
