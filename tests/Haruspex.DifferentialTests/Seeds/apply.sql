-- Seed for tests/fixtures/apply. Invented data: a small market. Rows that an APPLY finds nothing for are there on purpose,
-- so that every nullability claim about an APPLY's right side meets data that could break it: customer 2 has no
-- purchases (and no email or city), purchase 11 has no lines, purchase 13 is sent (not in the open view). Every nullable
-- column is null in some row a procedure returns: an email, a city, a note, a colour, a width, a height and a discount.
INSERT INTO mart.customer (customer_id, name, email, city)
VALUES (1, N'Ada', 'ada@example.test', N'Northam'),
       (2, N'Bo', NULL, NULL),
       (3, N'Cleo', NULL, N'Southby');

INSERT INTO mart.product (product_id, sku, title, colour, width_mm, height_mm)
VALUES (100, 'MUG-01', N'Plain mug', NULL, NULL, 95),
       (101, 'BOWL-02', N'Red bowl', N'red', 150, NULL),
       (102, 'JUG-03', N'Tall jug', N'blue', 90, 240);

INSERT INTO mart.purchase (purchase_id, customer_id, placed_on, status, note)
VALUES (10, 1, '2026-10-01', 'open', NULL),
       (11, 1, '2026-10-05', 'paid', N'gift wrap'),
       (12, 3, '2026-10-03', 'open', N'leave at door'),
       (13, 3, '2026-09-20', 'sent', NULL);

INSERT INTO mart.purchase_line (purchase_id, line_no, product_id, quantity, unit_price, discount)
VALUES (10, 1, 100, 2, 9.50, NULL),
       (10, 2, 101, 1, 3.25, 0.50),
       (12, 1, 101, 4, 3.25, NULL),
       (13, 1, 102, 1, 12.00, NULL);
