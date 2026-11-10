-- The calls fixture's seed: an oven without racks (rack_count null), batches with and without a weight and a note.
INSERT INTO calls.oven (oven_id, label, rack_count) VALUES (1, N'Deck', 4), (2, N'Rotary', NULL), (3, N'Convection', 2);
INSERT INTO calls.batch (batch_id, oven_id, product, baked_on, weight_g, note) VALUES
    (10, 1, N'Rye loaf', '2026-11-02', 800, N'crust dark'),
    (11, 1, N'Seeded roll', '2026-11-02', NULL, NULL),
    (12, 2, N'Brioche', '2026-11-03', 450, NULL),
    (13, 3, N'Baguette', '2026-11-03', 250, N'long proof');
