-- ROLLUP's subtotal rows make a grouped column null outside the aggregates, not inside them; HAVING proves an aggregate.
CREATE PROCEDURE kennel.rollup_and_having
AS
SELECT d.owner_id, COUNT(*) AS dogs, MAX(d.chip) AS top_chip
FROM kennel.dog AS d
WHERE d.chip IS NOT NULL AND d.owner_id IS NOT NULL
GROUP BY ROLLUP (d.owner_id);

SELECT o.owner_id, MAX(o.phone) AS phone
FROM kennel.owner AS o
GROUP BY o.owner_id
HAVING MAX(o.phone) > '';
