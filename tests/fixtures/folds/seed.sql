-- Seed for tests/fixtures/folds. Invented data: a small shop's products and tags. Product 1 has two tags and a
-- reference; product 2 no tags, no reference and no credits (so NULLIF(credits, 0) is NULL); products 3 and 4 the largest and
-- smallest budgets decimal(19,4) holds, and product 5 the smallest step, so each division gives its most digits after the
-- point. Product 3's one tag makes its first tag the only one.
INSERT INTO store.product (product_id, name, budget, credits, ref, ref_system)
VALUES (1, N'kettle', 1234.5678, 7, 42, 'erp'),
       (2, N'mug', 10.0000, 0, NULL, NULL),
       (3, N'boiler', 999999999999999.9999, 1, 7, NULL),
       (4, N'refund', -999999999999999.9999, 3, NULL, 'erp'),
       (5, N'sample', 0.0001, 9, NULL, NULL);

INSERT INTO store.tag (tag_id, product_id, name)
VALUES (1, 1, N'steel'),
       (2, 1, N'blue'),
       (3, 3, N'large');
