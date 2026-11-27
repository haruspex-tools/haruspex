-- Seed for tests/fixtures/nests. Invented data chosen to reach every case FOR JSON AUTO's nesting allows:
--
-- customer 1: orders 10 (two lines, a note) and 11 (one line, no note).
-- customer 2: order 12, with no lines, so an outer join's line object has no keys.
-- line 103 belongs to order 99, which isn't there, so a RIGHT JOIN's order object has no keys.
-- Lines ordered by sku put order 10's lines apart, so scattered_orders writes order 10 twice.
INSERT INTO nest.customers (customer_id, name)
VALUES (1, N'Ash'), (2, N'Birch');

INSERT INTO nest.orders (order_id, customer_id, placed_on, note)
VALUES (10, 1, '2026-01-01', N'leave at door'),
       (11, 1, '2026-01-02', NULL),
       (12, 2, '2026-01-03', NULL);

INSERT INTO nest.order_lines (line_id, order_id, sku, qty)
VALUES (100, 10, 'A-1', 1),
       (101, 10, 'C-3', 2),
       (102, 11, 'B-2', 3),
       (103, 99, 'D-4', 4);

INSERT INTO nest.notes (note_id, order_id, body)
VALUES (1, 10, N'fragile'),
       (2, 10, NULL);
