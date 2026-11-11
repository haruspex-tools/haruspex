-- Aggregates without GROUP BY, at the top level: one row always, and over no rows each aggregate is NULL, not [] or
-- {}. Returned as columns, so the driver delivers them as JSON text.
CREATE PROCEDURE pay.review_digest
AS
SET NOCOUNT ON;

SELECT json_arrayagg(r.review_id ORDER BY r.review_id) AS ids,
       json_objectagg(r.review_id: r.stars) AS stars
FROM pay.review AS r;
