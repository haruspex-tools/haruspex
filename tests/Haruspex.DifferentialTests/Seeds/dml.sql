-- Seed for tests/fixtures/dml. Invented data: a parts depot. Every nullable column a procedure returns is null in some row it
-- returns: a part with no note, one with no stock row (so no quantity or shelf), a stock row with no shelf, an order with no
-- note and one with no lines; and the price list differs from the prices in each way a MERGE acts on: a price to update, a
-- part to insert, a price to delete (not on the list), and a listed part with no price yet, which is left alone.
INSERT INTO depot.part (part_id, code, name, note)
VALUES (1, 'A-100', N'Bracket', NULL),
       (2, 'B-200', N'Hinge', N'fragile'),
       (3, 'C-300', N'Spring', NULL);

-- Part 1 is low (under 5); part 3 has no stock row.
INSERT INTO depot.stock (part_id, qty, shelf)
VALUES (1, 3, NULL),
       (2, 50, N'S1');

INSERT INTO depot.pick_order (placed_at, status, note)
VALUES ('2026-11-01T09:00:00', 'open', NULL),
       ('2026-11-02T10:30:00', 'picked', N'rush'),
       ('2026-11-03T11:15:00', 'open', N'no lines yet');

INSERT INTO depot.pick_line (order_id, part_id, qty)
VALUES (1, 1, 2),
       (2, 2, 1);

INSERT INTO depot.price (part_id, amount)
VALUES (1, 9.99),
       (2, 5.00),
       (4, 1.00);

INSERT INTO depot.price_list (part_id, amount)
VALUES (1, 9.99),
       (2, 6.00),
       (3, 7.50),
       (5, NULL);

INSERT INTO depot.legacy_note (note_id, text) VALUES (1, N'old');

INSERT INTO depot.error_log (procedure_name, message) VALUES (N'depot.seed', N'seeded');
