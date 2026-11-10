-- MERGE with a joined source: under WHEN NOT MATCHED BY SOURCE every table of the USING join is null for the rows it
-- deletes, the list's and the part's alike (review round 1, finding 1).
CREATE PROCEDURE depot.prune_prices
AS
SET NOCOUNT ON;

MERGE depot.price AS t
USING depot.price_list AS l JOIN depot.part AS p ON p.part_id = l.part_id ON t.part_id = l.part_id
WHEN MATCHED THEN UPDATE SET amount = t.amount
WHEN NOT MATCHED BY SOURCE THEN DELETE
OUTPUT $action AS change, deleted.part_id AS part, l.part_id AS listed_part, p.code;
