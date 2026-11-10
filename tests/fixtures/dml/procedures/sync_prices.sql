-- Synchronises the prices with the list, through a CTE before MERGE, and returns what changed: $action from the actions it
-- has, the new and old part (null for the rows deleted or inserted), and the source's amount, which the CTE proves is not
-- null but which is null for the rows deleted because the list does not have them.
CREATE PROCEDURE depot.sync_prices
AS
SET NOCOUNT ON;

WITH listed AS
(
    SELECT l.part_id, l.amount FROM depot.price_list AS l WHERE l.amount IS NOT NULL
)
MERGE depot.price AS t
USING listed AS s ON t.part_id = s.part_id
WHEN MATCHED AND t.amount <> s.amount THEN UPDATE SET amount = s.amount
WHEN NOT MATCHED BY TARGET THEN INSERT (part_id, amount) VALUES (s.part_id, s.amount)
WHEN NOT MATCHED BY SOURCE THEN DELETE
OUTPUT $action AS change, inserted.part_id AS new_part, deleted.part_id AS old_part, inserted.amount AS new_amount, s.amount AS listed_amount;
