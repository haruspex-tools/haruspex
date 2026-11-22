-- Seed for tests/fixtures/values. Invented data chosen to reach the edge cases: every type's maximum and minimum
-- (and strings of their full length),
-- non-zero and zero fractions of a second, non-zero and zero offsets, padded and empty fixed-length strings, an nchar(5)
-- holding two characters outside the Basic Multilingual Plane (5 UTF-16 code units with its padding, but 3 code points), a
-- row whose every nullable column is null, and rows where only part of a nested object is null.
INSERT INTO vals.sample
    (sample_id, label, tiny, small, whole, big, dec_19_2, dec_38_0, num_9_3, dec_5_5, cash, small_cash, approx, approx_real,
     flag, day, t0, t3, t7, dt2_0, dt2_3, dt2_7, dto_0, dto_3, dto_7, small_dt, legacy_dt, guid, code, ncode, vcode, body)
VALUES
    (1, N'typical', 7, -12, 42, 1234567890123, 12.50, 1, 1.5, 0.5, 12.5, 1.25, 0.1, 0.5,
     1, '2026-03-04', '05:06:07', '05:06:07.1', '05:06:07.1234567',
     '2026-03-04T05:06:07', '2026-03-04T05:06:07.123', '2026-03-04T05:06:07.1234567',
     '2026-03-04T05:06:07+05:30', '2026-03-04T05:06:07.5-08:00', '2026-03-04T05:06:07.1234567+01:00',
     '2026-03-04T05:06:00', '2026-03-04T05:06:07.997', 'c0ffee00-1234-4abc-8def-0123456789ab',
     'AB', NCHAR(0xD83D) + NCHAR(0xDE00) + NCHAR(0xD83D) + NCHAR(0xDE00), 'AB', N'with "quotes", a \ and a line break' + NCHAR(10)),
    (2, N'maxima: every type at its largest values', 255, 32767, 2147483647, 9223372036854775807, 99999999999999999.99,
     99999999999999999999999999999999999999, 999999.999, 0.99999, 922337203685477.5807, 214748.3647, 1.79e308, 3.40e38,
     1, '9999-12-31', '23:59:59', '23:59:59.999', '23:59:59.9999999',
     '9999-12-31T23:59:59', '9999-12-31T23:59:59.999', '9999-12-31T23:59:59.9999999',
     '9999-12-31T23:59:59+14:00', '9999-12-31T23:59:59.999+14:00', '9999-12-31T23:59:59.9999999+14:00',
     '2079-06-06T23:59:00', '9999-12-31T23:59:59.997', 'FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF',
     'ABCDE', N'ABCDE', 'ABCDE', N'x'),
    (3, N'minima', 0, -32768, -2147483648, -9223372036854775808, -99999999999999999.99,
     -99999999999999999999999999999999999999, -999999.999, -0.99999, -922337203685477.5808, -214748.3648, -1.79e308, -3.40e38,
     0, '0001-01-01', '00:00:00', '00:00:00', '00:00:00',
     '0001-01-01T00:00:00', '0001-01-01T00:00:00', '0001-01-01T00:00:00',
     '0001-01-01T00:00:00-14:00', '0001-01-01T00:00:00-14:00', '0001-01-01T00:00:00-14:00',
     '1900-01-01T00:00:00', '1753-01-01T00:00:00', '00000000-0000-0000-0000-000000000000',
     '', N'', '', N''),
    (4, N'zero fractions and offsets', 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
     0, '2026-03-04', '05:06:07', '05:06:07', '05:06:07',
     '2026-03-04T05:06:07', '2026-03-04T05:06:07', '2026-03-04T05:06:07',
     '2026-03-04T05:06:07+00:00', '2026-03-04T05:06:07+00:00', '2026-03-04T05:06:07+00:00',
     '2026-03-04T05:06:00', '2026-03-04T05:06:07', 'C0FFEE00-1234-4ABC-8DEF-0123456789AB',
     'A    ', N'A', 'A', N'{"looks":"like json"}');

-- Every nullable column null.
INSERT INTO vals.sample (sample_id, label) VALUES (5, N'nulls');

-- Nested objects only partly null: a time but no day, a flag but no whole number, a guid (row 6); and the
-- reverse (row 7).
INSERT INTO vals.sample (sample_id, label, t0, flag, guid) VALUES (6, N'partial', '05:06:07', 0, 'c0ffee00-1234-4abc-8def-0123456789ab');
INSERT INTO vals.sample (sample_id, label, day, whole) VALUES (7, N'partial reversed', '2026-03-04', 1);

-- Keys at int's edges, so that every id field reaches its type's bounds; otherwise null.
INSERT INTO vals.sample (sample_id, label) VALUES (-2147483648, N'lowest key'), (2147483647, N'highest key');

-- Tags: two for sample 1, one with a note and one without, one for sample 2 with a name and a note of the full
-- length, one each for the samples at the key's edges (with tag ids at theirs), none for the rest.
INSERT INTO vals.sample_tag (sample_tag_id, sample_id, name, note)
VALUES (1, 1, N'red', N'bright'), (2, 1, N'blue', NULL), (3, 2, REPLICATE(N'g', 20), REPLICATE(N'p', 40)),
       (-2147483648, -2147483648, N'low', NULL), (2147483647, 2147483647, N'high', NULL);
