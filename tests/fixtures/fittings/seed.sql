-- Seed for tests/fixtures/fittings. Invented data: three customers of a tailor, the first at each CHECK's lower edge, the second
-- at its upper edge, and the third with every nullable column NULL and an empty string where one is allowed. The differential
-- tests read them as a user without UNMASK, who receives the masks instead.
INSERT INTO tailor.customer (customer_id, name, email, phone, band, size_code, chest_cm, born, joined, swatch, is_trade, note)
VALUES (1, N'Ada', 'ada@example.com', '555-0100-1234', 1, 'S08', 60.0, '1980-02-03', '2020-05-06T07:08:09', 0x0A0B0C0D, 1, N'Prefers wool'),
       (2, N'Bram', 'bram@example.org', '555-0199', 3, 'L12', 160.0, '1999-12-31', '2026-11-24T23:59:59', 0xFFFFFFFF, 1, N''),
       (3, N'Cleo', '', NULL, 2, 'M10', 99.5, NULL, '2001-01-01T00:00:00', NULL, 1, NULL);
