-- An aggregate of objects whose null keys are left out, and a record keyed by a number, under GROUP BY.
CREATE PROCEDURE pay.reviews_by_book
AS
SET NOCOUNT ON;

SELECT r.book_id AS bookId,
       json_arrayagg(json_object('id': r.review_id, 'stars': r.stars, 'comment': r.comment ABSENT ON NULL) ORDER BY r.review_id) AS reviews,
       json_objectagg(r.review_id: r.comment) AS comments,
       -- ABSENT ON NULL drops every null comment, so a group whose comments are all null gives [].
       json_arrayagg(r.comment ORDER BY r.review_id) AS commentList
FROM pay.review AS r
GROUP BY r.book_id
ORDER BY r.book_id
FOR JSON PATH;
