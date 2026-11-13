-- CROSS APPLY (VALUES …) reading the left side: each book gives two rows, one per kind of contact.
CREATE PROCEDURE lib.apply_values
AS
SET NOCOUNT ON;

SELECT b.book_id, v.kind, v.who
FROM lib.book AS b
CROSS APPLY (VALUES ('donor', b.donor_id), ('self', b.book_id)) AS v (kind, who)
ORDER BY b.book_id, v.kind;
