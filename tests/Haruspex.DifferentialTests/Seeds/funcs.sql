-- Seed for tests/fixtures/funcs. Invented data: a small gym. Every nullable input is NULL at least once: member 2 and 4 have
-- no nickname, last visit or birth date, member 4 no prefs. The plus plan's members (2 and 4) have no nickname at all, so its
-- STRING_AGG is NULL. Prefs give JSON_VALUE a string, a JSON null, an object, nothing, and a string longer than 4,000
-- characters. Member 1 has three visits (one untimed), member 2 two, both untimed (so its SUM and percentiles are NULL), member
-- 3 one, members 4 and 5 none; first and last visits of each member leave the framed windows and LAG/LEAD empty. Rooms are
-- never 0, so fee / room cannot divide by zero.
INSERT INTO gym.member (member_id, name, nickname, code, plan_name, joined, last_seen, born, prefs)
VALUES (1, 'Ann', N'annie', 'A1', 'basic', '2024-03-15', '2026-11-01 08:15', '1990-07-04', N'{"locker":"L7"}'),
       (2, 'Bax', NULL, 'B2', 'plus', '2025-01-31', NULL, NULL, N'{"locker":null}'),
       (3, 'Cyx', N' cy ', 'C3', 'basic', '2023-12-01', '2026-10-10 19:45', '2001-02-28', N'{"locker":{"row":2}}'),
       (4, 'Dee', NULL, 'D4', 'plus', '2026-02-28', NULL, NULL, NULL),
       (5, 'Xavex', N'Ox', 'E5', 'basic', '2022-08-08', '2026-11-09 06:30', '1985-12-31', N'{"locker":"' + REPLICATE(CAST(N'x' AS nvarchar(max)), 5000) + N'"}');

INSERT INTO gym.visit (visit_id, member_id, started_at, minutes, room, fee, tip, rating, weight)
VALUES (1, 1, '2026-11-01 08:00', 60, 1, 10.00, 1.50, 4.5, 70.5),
       (2, 1, '2026-11-03 18:30', NULL, 2, 12.00, NULL, NULL, NULL),
       (3, 1, '2026-11-05 07:10', 45, 3, 8.00, 0, 3, 70),
       (4, 2, '2026-11-02 12:00', NULL, 1, 5.00, NULL, NULL, NULL),
       (5, 2, '2026-11-04 12:00', NULL, 2, 5.00, NULL, 2.5, NULL),
       (6, 3, '2026-11-06 20:00', 30, 3, 9.99, 2.00, 5, 55.25);
