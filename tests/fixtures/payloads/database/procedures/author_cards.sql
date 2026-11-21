-- The cards author_card_rows returns, collected into a table variable by INSERT … EXEC and returned as one JSON document.
-- The callee's columns have other names than the table's, and the column list is in another order than the table's: each
-- goes into the column the list names in its place, by position.
CREATE PROCEDURE pay.author_cards @first int, @last int
AS
SET NOCOUNT ON;

DECLARE @cards TABLE
(
    author_id int           NOT NULL PRIMARY KEY,
    books     nvarchar(max) NULL,
    about     nvarchar(max) NULL,
    card      nvarchar(max) NULL
);

INSERT INTO @cards (author_id, card, about, books)
EXEC pay.author_card_rows @first, @last;

-- A range with no author gives a placeholder for its first id, with no card: NULL joins with what INSERT … EXEC wrote.
IF @@ROWCOUNT = 0 AND @first IS NOT NULL
    INSERT INTO @cards (author_id, card, about, books) VALUES (@first, NULL, NULL, NULL);

SELECT c.author_id AS authorId,
       json_query(c.card) AS card,
       json_query(c.about) AS about,
       json_query(c.books) AS books
FROM @cards AS c
ORDER BY c.author_id
FOR JSON PATH;
