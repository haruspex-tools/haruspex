-- Statements that return nothing but change what runs: a GOTO that skips a result on one path, so the results after it move
-- up; SET ROWCOUNT 1, which leaves the latest sale at most one row; and WAITFOR DELAY and SET LANGUAGE, which change no type.
-- DATENAME writes the month in British English here, and only its type is claimed.
CREATE PROCEDURE flow.sale_digest
    @brief bit = 0
AS
SET NOCOUNT ON;
SET LANGUAGE British;
WAITFOR DELAY '00:00:00.010';

DECLARE @scope nvarchar(10) = N'every sale';
IF @brief = 1
BEGIN
    SET @scope = N'the latest';
    GOTO latest;
END

SELECT s.sale_id, s.channel FROM flow.sale AS s ORDER BY s.sale_id;

latest:
SET ROWCOUNT 1;
SELECT s.sale_id, s.amount, DATENAME(month, CAST('2026-03-02' AS date)) AS month FROM flow.sale AS s ORDER BY s.sale_id DESC;
SET ROWCOUNT 0;

SELECT @scope AS scope;
