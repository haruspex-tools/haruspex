-- The payload returned as a column, as many API procedures return it: a driver delivers it as text to parse.
CREATE PROCEDURE pay.author_documents
AS
SET NOCOUNT ON;

SELECT a.author_id AS id,
       json_object('name': a.name, 'bio': a.bio) AS document,
       a.profile
FROM pay.author AS a
ORDER BY a.author_id;
