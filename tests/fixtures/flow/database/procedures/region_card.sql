-- A table variable keyed by its declaration (a primary key and a unique index on code), left joined to a temporary table keyed
-- by its CREATE TABLE: fixing the variable's key, and the temporary table's by the join, reads at most one row. The temporary
-- table's key has no name of its own: a constraint's name is an object of tempdb's schema, which two calls would share.
CREATE PROCEDURE flow.region_card
    @region_id int
AS
SET NOCOUNT ON;

DECLARE @regions TABLE (
    region_id int PRIMARY KEY,
    code char(3) NOT NULL,
    name nvarchar(30) NOT NULL,
    INDEX ux_regions_code UNIQUE (code)
);

INSERT INTO @regions (region_id, code, name)
SELECT r.region_id, r.code, r.name FROM flow.region AS r;

CREATE TABLE #totals (
    region_id int NOT NULL,
    total decimal(38, 2) NULL,
    PRIMARY KEY (region_id)
);

INSERT INTO #totals (region_id, total)
SELECT s.region_id, SUM(s.amount) FROM flow.sale AS s GROUP BY s.region_id;

SELECT r.code, r.name, t.total
FROM @regions AS r
LEFT JOIN #totals AS t ON t.region_id = r.region_id
WHERE r.region_id = @region_id;
