-- One author as a payload built the way hand-written API procedures build them: json_query(json_object(...)), with a
-- nested aggregate of objects, a FOR JSON PATH subquery inside the constructor, and a native json column.
CREATE PROCEDURE pay.get_author
    @author_id int
AS
SET NOCOUNT ON;

SELECT json_query(json_object(
           'id': a.author_id,
           'name': a.name,
           'bio': a.bio,
           'profile': a.profile,
           'books': (
               SELECT json_arrayagg(json_object(
                          'id': b.book_id,
                          'title': b.title,
                          'isbn': b.isbn,
                          'price': b.price,
                          'published': b.published
                          ABSENT ON NULL)
                      ORDER BY b.book_id)
               FROM pay.book AS b
               WHERE b.author_id = a.author_id
           ),
           'reviews': json_query((
               SELECT r.review_id AS id,
                      r.stars,
                      r.comment
               FROM pay.review AS r
               JOIN pay.book AS rb ON rb.book_id = r.book_id
               WHERE rb.author_id = a.author_id
               ORDER BY r.review_id
               FOR JSON PATH
           ))
       )) AS author
FROM pay.author AS a
WHERE a.author_id = @author_id
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
