-- Seed for tests/fixtures/bakehouse. Invented data: a shop with two sales, one with one, and one with none, so a nested list
-- is seen with several objects, with one, and left out.
INSERT INTO bake.shop (shop_id, name)
VALUES (1, N'Crumb Lane'),
       (2, N'Oven Row'),
       (3, N'Flour Yard');

INSERT INTO bake.sale (sale_id, shop_id, loaves)
VALUES (10, 1, 3),
       (11, 1, 1),
       (12, 2, 12);
