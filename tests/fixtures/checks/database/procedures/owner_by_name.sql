-- A lookup by a key CREATE UNIQUE INDEX adds after the table: at most one row.
CREATE PROCEDURE chk.owner_by_name
    @name nvarchar(20)
AS
SET NOCOUNT ON;

SELECT o.name
FROM chk.owner AS o
WHERE o.name = @name;
