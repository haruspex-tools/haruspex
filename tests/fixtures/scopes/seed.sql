-- Seed for tests/fixtures/scopes. Invented data: a small lending library. Member 4 has no loans and no email, so every
-- outer join and OUTER APPLY over loans has a null-extended row; book 11 has no page count and no donor; loans 2 and 3
-- are not returned; member 3 has returned a book they donated.
INSERT INTO lib.member (member_id, name, email, referred_by)
VALUES (1, N'Ada', 'ada@example.test', NULL),
       (2, N'Bo', NULL, 1),
       (3, N'Cy', 'cy@example.test', 2),
       (4, N'Di', NULL, 1);

INSERT INTO lib.book (book_id, title, pages, donor_id)
VALUES (10, N'Atlas', 320, 1),
       (11, N'Bestiary', NULL, NULL),
       (12, N'Codex', 90, 3),
       (13, N'Diary', 150, 1);

INSERT INTO lib.loan (member_id, book_id, loaned_on, returned_on)
VALUES (1, 12, '2026-10-01', '2026-10-10'),
       (1, 13, '2026-10-15', NULL),
       (2, 10, '2026-10-05', NULL),
       (3, 11, '2026-10-07', '2026-10-20'),
       (3, 12, '2026-10-21', '2026-10-25');

INSERT INTO dbo.shelf (shelf_id, label)
VALUES (1, 'A1'),
       (2, 'B2');
