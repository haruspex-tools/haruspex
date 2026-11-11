-- Aggregates under GROUP BY: every group has a row, so they are never null.
CREATE PROCEDURE pay.author_stats
AS
SET NOCOUNT ON;

SELECT b.author_id AS authorId,
       json_arrayagg(b.book_id ORDER BY b.book_id) AS bookIds,
       json_objectagg(b.title: b.price) AS prices
FROM pay.book AS b
GROUP BY b.author_id
ORDER BY b.author_id
FOR JSON PATH;
