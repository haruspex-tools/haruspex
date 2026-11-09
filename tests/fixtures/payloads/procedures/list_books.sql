-- Constructors as FOR JSON PATH columns: json_array as a tuple, as an array of the elements' types, and with NULL ON
-- NULL; correlated aggregates in a json_object with ABSENT ON NULL; json_objectagg with data keys; nesting.
CREATE PROCEDURE pay.list_books
AS
SET NOCOUNT ON;

SELECT b.book_id AS id,
       json_array(b.book_id, b.price) AS pair,
       json_array(b.title, b.isbn) AS labels,
       json_array(b.title, b.isbn, b.published NULL ON NULL) AS slots,
       json_object(
           'stars': (SELECT json_arrayagg(r.stars ORDER BY r.review_id) FROM pay.review AS r WHERE r.book_id = b.book_id),
           'comments': (SELECT json_arrayagg(r.comment ORDER BY r.review_id NULL ON NULL) FROM pay.review AS r WHERE r.book_id = b.book_id),
           'latest': (
               SELECT TOP (1) json_object('stars': r.stars, 'posted': r.posted)
               FROM pay.review AS r
               WHERE r.book_id = b.book_id
               ORDER BY r.posted DESC
           )
           ABSENT ON NULL) AS reviews,
       (SELECT json_objectagg(t.tag: t.weight) FROM pay.book_tag AS t WHERE t.book_id = b.book_id) AS tags,
       -- Without its array wrapper a FOR JSON subquery nests as text, in FOR JSON and in a constructor alike, until
       -- json_query makes it JSON again.
       (
           SELECT TOP (1) r.review_id AS id, r.stars
           FROM pay.review AS r WHERE r.book_id = b.book_id ORDER BY r.review_id
           FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
       ) AS firstReviewText,
       json_object(
           'text': (
               SELECT TOP (1) r.review_id AS id, r.stars
               FROM pay.review AS r WHERE r.book_id = b.book_id ORDER BY r.review_id
               FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
           ),
           'live': json_query((
               SELECT TOP (1) r.review_id AS id, r.stars
               FROM pay.review AS r WHERE r.book_id = b.book_id ORDER BY r.review_id
               FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
           ))) AS firstReview,
       json_query(json_object('author': json_object('id': a.author_id, 'name': a.name))) AS meta
FROM pay.book AS b
JOIN pay.author AS a ON a.author_id = b.author_id
ORDER BY b.book_id
FOR JSON PATH;
