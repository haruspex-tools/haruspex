-- Columns named by their tables' schema and name, in the select list, ON, WHERE, ORDER BY and a subquery reaching outward:
-- typed as an alias would type them.
CREATE PROCEDURE lib.full_names
AS
SELECT lib.book.title,
       lib.book.pages,
       lib.member.name AS donor,
       (SELECT COUNT_BIG(*) FROM lib.loan WHERE lib.loan.book_id = lib.book.book_id) AS loans
FROM lib.book
LEFT JOIN lib.member ON lib.member.member_id = lib.book.donor_id
WHERE lib.book.pages IS NOT NULL
ORDER BY lib.book.title;
