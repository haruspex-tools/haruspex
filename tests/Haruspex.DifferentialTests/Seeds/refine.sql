-- Seed for tests/fixtures/refine. Invented data: a lending library. Every nullable column is null in some row that a
-- procedure returns, where its filters allow one: a branch with no city, a title with no ISBN, author or genre, a member
-- with no email, nickname or home branch, a copy with no shelf, and loans to no member, with no due date, not returned
-- and with no fine. Only one title may have no ISBN and one member no email: a unique constraint allows one NULL.
INSERT INTO lend.branch (branch_id, name, city)
VALUES (1, N'Central', N'Riverton'),
       (2, N'Harbour', NULL),
       (3, N'Hillside', N'Oakfield'),
       (4, N'Meadow', N'Fairview');

-- D. Poet's only title has no ISBN, so the first ISBN of that author is null; C. Nobody has no genre at all.
INSERT INTO lend.title (title_id, isbn, name, author, genre)
VALUES (1, '9780000000011', N'The Long Road', N'A. Writer', 'fiction'),
       (2, NULL, N'Tides', N'D. Poet', 'fiction'),
       (3, '9780000000035', N'Stars', NULL, 'science'),
       (4, '9780000000042', N'Old Kingdoms', N'B. Scholar', 'history'),
       (5, '9780000000059', N'Notes', N'C. Nobody', NULL),
       (6, '9780000000066', N'Short Road', N'A. Writer', NULL);

INSERT INTO lend.member (member_id, email, name, nickname, home_branch_id)
VALUES (1, 'ann@example.test', N'Ann', N'Annie', 1),
       (2, NULL, N'Bob', NULL, NULL),
       (3, 'cy@example.test', N'Cy', NULL, 2),
       (4, 'di@example.test', N'Di', N'D', 3);

-- Branch 3 has only a copy with no shelf; branch 4 has none.
INSERT INTO lend.copy (branch_id, copy_no, title_id, shelf)
VALUES (1, 1, 1, 'A1'),
       (1, 2, 2, NULL),
       (2, 1, 3, 'B7'),
       (2, 2, 4, NULL),
       (3, 1, 5, NULL);

-- Di never borrowed. Loan 4 went to another library (no member); loan 3 has no due date. Cy's latest loan is back, with
-- no fine.
INSERT INTO lend.loan (loan_id, branch_id, copy_no, member_id, lent_on, due_on, returned_on, fine)
VALUES (1, 1, 1, 1, '2026-09-01', '2026-09-15', '2026-09-10', NULL),
       (2, 1, 2, 1, '2026-10-01', '2026-10-15', NULL, 1.50),
       (3, 2, 1, 2, '2026-10-05', NULL, NULL, NULL),
       (4, 2, 2, NULL, '2026-10-07', '2026-11-01', NULL, NULL),
       (5, 1, 1, 3, '2026-10-20', '2026-11-03', NULL, 0.00),
       (6, 3, 1, 2, '2026-11-01', '2026-11-05', NULL, NULL),
       (7, 2, 2, 3, '2026-11-02', '2026-11-09', '2026-11-08', NULL);
