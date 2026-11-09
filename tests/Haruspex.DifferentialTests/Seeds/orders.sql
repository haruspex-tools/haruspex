-- Seed for tests/fixtures/orders. Invented data chosen to reach the edge cases: a customer with no email or
-- region, a customer with no orders, an order with no lines and no salesperson, lines with and without notes,
-- a product with no weight, and a legacy code above 2^53.
INSERT INTO hr.salesperson (salesperson_id, display_name, hired_on) VALUES (1, N'Dee Holt', '2019-04-01');

SET IDENTITY_INSERT sales.customer ON;
INSERT INTO sales.customer (customer_id, name, email, region_code, created_at)
VALUES (1, N'Ada Fenwick', N'ada@example.com', 'NW', '2025-11-02T08:15:00'),
       (2, N'Bram Oddie', NULL, NULL, '2025-12-24T17:00:00'),
       (3, N'Cleo Marsh', N'cleo@example.org', 'S', '2026-01-10T12:00:00');
SET IDENTITY_INSERT sales.customer OFF;

INSERT INTO sales.product (product_id, sku, name, weight_kg, legacy_code, picture)
VALUES (1, 'WID-1', N'Widget', 1.250, 9007199254740993, NULL),
       (2, 'GAD-2', N'Gadget', NULL, NULL, 0x89504E47);

INSERT INTO sales.[order]
    (order_id, customer_id, salesperson_id, reference, placed_at, shipped_at, delivery_time, status, total, is_priority, notes)
VALUES (100, 1, 1, 'a1b2c3d4-0000-4000-8000-000000000100', '2026-01-05T09:30:00', '2026-01-06T10:00:00+00:00', '14:30:00', 'shipped', 41.50, 1, N'{"gift":true}'),
       (101, 1, NULL, 'a1b2c3d4-0000-4000-8000-000000000101', '2026-01-07T11:00:00', NULL, NULL, 'open', 0.00, 0, NULL),
       (102, 2, 1, 'a1b2c3d4-0000-4000-8000-000000000102', '2026-01-31T23:59:59', '2026-02-01T08:00:00-05:00', '09:00:00', 'shipped', 15.00, 0, NULL);

INSERT INTO sales.order_line (order_line_id, order_id, product_id, quantity, unit_price, note)
VALUES (1000, 100, 1, 2, 12.50, N'gift wrap'),
       (1001, 100, 2, 1, 16.50, NULL),
       (1002, 102, 2, 3, 5.00, NULL);
