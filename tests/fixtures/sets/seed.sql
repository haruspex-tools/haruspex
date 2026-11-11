-- Seed for tests/fixtures/sets. Invented data: a small ice rink. Every query of every set operation gives a NULL somewhere:
-- member 2 and 4 have no email, guest 2 no phone, member 3 no level; a guest's lesson booking has no member. Member 3 and
-- member 5 have no locker, locker 2 no member, so the full join is unmatched on both sides. Prefs miss a path (member 2's
-- theme), hold a JSON null under a strict path (member 2's skate_size), an object, an array and a scalar under AS JSON, and
-- member 4 has none; booking 10's extras have one value of each OPENJSON type, booking 11 none. Session 4 has no bookings.
INSERT INTO rink.member (member_id, name, email, level, prefs)
VALUES (1, 'Ada', 'ada@example.org', 'coach', N'{"theme":"dark","skate_size":38,"notify":{"email":true}}'),
       (2, 'Bo', NULL, 'novice', N'{"skate_size":null,"notify":[1,2]}'),
       (3, 'Cy', 'cy@example.org', NULL, N'{"theme":"light","skate_size":44,"notify":"no"}'),
       (4, 'Di', NULL, 'skater', NULL),
       (5, 'Eve', 'eve@example.org', 'novice', N'{"theme":"dark","skate_size":40}');

INSERT INTO rink.guest (guest_id, name, phone)
VALUES (1, 'Gus', '0700 111'),
       (2, 'Hal', NULL);

INSERT INTO rink.session (session_id, starts_at, kind, coach_id)
VALUES (1, '2026-11-14 10:00', 'public', NULL),
       (2, '2026-11-14 12:00', 'lesson', 1),
       (3, '2026-11-14 18:00', 'hockey', 1),
       (4, '2026-11-15 10:00', 'public', NULL);

INSERT INTO rink.booking (booking_id, session_id, member_id, guest_id, skates, extras)
VALUES (10, 1, 1, NULL, 2, N'{"locker":true,"note":null,"items":["gloves"],"hire":{"skates":2},"code":"A1","count":3}'),
       (11, 2, 3, NULL, NULL, NULL),
       (12, 2, NULL, 1, 1, N'[]'),
       (13, 3, 1, NULL, NULL, N'{"code":"B2"}'),
       (14, 1, NULL, 2, 3, NULL);

INSERT INTO rink.locker (locker_no, member_id, size)
VALUES (1, 1, 'S'),
       (2, NULL, 'L');
