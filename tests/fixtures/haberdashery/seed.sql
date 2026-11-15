-- Seed for tests/fixtures/haberdashery. Invented data: a haberdasher's orders of ribbon, buttons and thread, a price list
-- and parcels. Chosen so that every nullable column is null somewhere and set somewhere else, an order has no lines, and a
-- parcel has no sparse value set (its column set is then NULL).
INSERT INTO shop.orders (order_id, customer, placed_on, note)
VALUES (1, N'Ada Thimble', '2026-03-02', N'Gift wrap, please'),
       (2, N'Bram Needle', '2026-03-05', NULL),
       (3, N'Cleo Bobbin', '2026-03-09', N'Leave with the neighbour');

INSERT INTO shop.order_line (line_id, order_id, sku, qty)
VALUES (10, 1, 'RIB-SAT-25', 3),
       (11, 1, 'BTN-HORN-18', NULL),
       (12, 2, 'THR-COT-40', 1);

INSERT INTO shop.price (sku, amount)
VALUES ('RIB-SAT-25', 1.20),
       ('BTN-HORN-18', 0.45);

INSERT INTO shop.parcel (parcel_id, weight_g, label, fragile)
VALUES (1, 250, N'Ribbons', 0),
       (2, NULL, NULL, NULL),
       (3, 900, N'Buttons', NULL);
