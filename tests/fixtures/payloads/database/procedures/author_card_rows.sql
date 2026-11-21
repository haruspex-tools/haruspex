-- Author cards as a plain rowset, the JSON in columns, for a caller to collect with INSERT … EXEC: a FOR JSON result would
-- fail that insert (13602). book_list falls back to an empty array, so it is never null; bio_card is null without a bio.
CREATE PROCEDURE pay.author_card_rows @first int, @last int
AS
SET NOCOUNT ON;

SELECT a.author_id,
       json_object('name': a.name) AS name_card,
       CASE WHEN a.bio IS NOT NULL THEN json_object('bio': a.bio) END AS bio_card,
       json_query(coalesce((SELECT b.book_id AS bookId, b.title
                            FROM pay.book AS b
                            WHERE b.author_id = a.author_id
                            ORDER BY b.book_id
                            FOR JSON PATH), N'[]')) AS book_list
FROM pay.author AS a
WHERE a.author_id BETWEEN @first AND @last;
