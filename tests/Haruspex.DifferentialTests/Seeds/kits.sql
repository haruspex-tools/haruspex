-- Seed for tests/fixtures/kits. Invented data: a craft-kit catalogue. Kit 1 has three parts (one without a colour, one with an
-- eight-character sku and a ten-character colour, one with qty 255); kit 2 has no parts, so a FOR JSON subquery over its parts
-- is NULL and every literal fallback is taken, and it is free; kit 3 has one part and stored extras; kit 4 has no parts
-- either, but no fallback is written for it, so its parts stay NULL. A kit id that is not here (99) leaves a
-- variable assigned by a SELECT as it was.
INSERT INTO kit.kit (kit_id, name, price, extras)
VALUES (1, N'Lantern', 24.50, NULL),
       (2, N'Empty box', 0.00, NULL),
       (3, REPLICATE(N'k', 40), 9999999.99, N'{"note": "stored"}'),
       (4, N'Spare kit', 1.00, NULL);

INSERT INTO kit.kit_part (part_id, kit_id, sku, qty, colour)
VALUES (10, 1, 'LN-01', 2, NULL),
       (11, 1, 'LN-02-XL', 1, N'deep green'),
       (12, 1, 'LN', 255, N'red'),
       (30, 3, 'K3', 0, N'blue');
