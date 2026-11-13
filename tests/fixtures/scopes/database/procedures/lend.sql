-- OUTPUT inserted.* names the target's columns, as they are after the write.
CREATE PROCEDURE lib.lend @member_id int, @book_id int, @returned_on date
AS
SET NOCOUNT ON;

INSERT INTO lib.loan (member_id, book_id, loaned_on, returned_on)
OUTPUT inserted.*
VALUES (@member_id, @book_id, '2026-11-01', @returned_on);
