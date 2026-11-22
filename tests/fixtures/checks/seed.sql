-- Seed for tests/fixtures/checks. Invented data reaching every edge the IR claims: each bound (an exclusive one at
-- the nearest step inside it), each value of each CHECK set, each maximum and minimum length, and null in every
-- nullable column. Some values pass a string CHECK without being one of its strings exactly ('CLOSED',
-- 'open      ', 'sw', 'b'): the database compares under the collation and ignores trailing spaces, as it does
-- under a binary one ('c+d   '), and on the literal's side too ('(x)' passes IN ('(x) ')). Ticket 5,
-- written by tests/fixtures/checks/database/tables/ticket_late.sql, breaks a constraint added after it WITH NOCHECK.
INSERT INTO chk.owner (owner_id, name, grade)
VALUES (1, N'Al', N'A'),
       (2, REPLICATE(N'n', 20), N'b');

INSERT INTO chk.ticket
    (ticket_id, owner_id, code, alt_code, status, priority, score, level, discount, rate, big, title, note, region, sigil,
     tag_utf8, pad_utf8, fill_utf8, muted, restored, late)
VALUES
    -- Lower edges.
    (-2147483648, 1, N'ABCDEFGH', N'ZYXWVUTS', 'open', 1, -10, 1, 0, 0.0001, 0, N'x', 'n', 'NE', 'a.b',
     'abcdefghijkl', 'ABCD', NCHAR(0xD83D) + NCHAR(0xDE00) + NCHAR(0xD83D) + NCHAR(0xDE00), -2147483648, 1, NULL),
    -- Upper edges. Six two-byte characters fill varchar(12) in UTF-8; one fills char(4) with two spaces. (Above, two emoji fill
    -- char(8) in UTF-8: 8 bytes, 2 code points.)
    (2147483647, 2, N'A', NULL, 'held', 5, 99, 2, 100, 0.9999, 9223372036854775807, REPLICATE(N'T', 60), REPLICATE('n', 30), 'sw', 'c+d   ',
     REPLICATE(N'é', 6), N'é', 'abcdefgh', 2147483647, 2147483647, 2147483647),
    -- Everything nullable null, no owner, no events, and a status in another case.
    (3, NULL, N'C3', NULL, 'CLOSED', NULL, NULL, 4, NULL, NULL, NULL, N'Three', NULL, NULL, '(x)',
     NULL, NULL, DEFAULT, NULL, NULL, NULL),
    -- A status with trailing spaces, at the column's full length; and a value ck_ticket_muted would refuse, written
    -- after ticket_nocheck.sql disabled it.
    (4, NULL, N'D4', NULL, 'open      ', NULL, NULL, NULL, NULL, NULL, NULL, N'Four', NULL, NULL, NULL,
     NULL, NULL, DEFAULT, -5, NULL, NULL);

INSERT INTO chk.ticket_event (event_id, ticket_id, kind, minutes)
VALUES (-2147483648, -2147483648, N'open', 1),
       (2, -2147483648, N'note', NULL),
       (2147483647, 2147483647, N'done', 32767);

-- chk.legacy_tag, created with ANSI_PADDING off: short values (cut in code and stamp, padded in kept and wide), full ones,
-- null in every nullable column, and the key's edges.
INSERT INTO chk.legacy_tag (tag_id, code, kept, wide, stamp)
VALUES (-2147483648, 'a', 'a', N'a', 0x01),
       (2, 'abcd', 'abcd', N'abcd', 0x01020304),
       (2147483647, NULL, '', NULL, NULL);
