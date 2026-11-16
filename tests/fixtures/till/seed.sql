-- Seed for tests/fixtures/till: a few products, one without a label.
INSERT INTO till.product (product_id, sku, name, label, price)
VALUES (1, 'TEA-0001', N'Tea, loose', 'tea', 4.50),
       (2, 'TEA-0002', N'Tea bags', NULL, 2.25),
       (3, 'CUP-0001', N'Cup', 'cups', 6.00);
