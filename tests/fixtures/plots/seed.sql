-- Seed for tests/fixtures/plots. Invented beds and sowings: a bed with two, one harvested and one not; a bed with none and
-- no area, so the outer join's unmatched row and a NULL area are both reached; a bed with one, harvested; and a bed with
-- no area and one, harvested, so a grouped SUM of the area is NULL.
INSERT INTO plot.bed (bed_id, name, area_m2, soil)
VALUES (1, N'North', 12.50, 'clay'),
       (2, N'South', NULL, 'loam'),
       (3, N'Shed side', 3.00, 'sand'),
       (4, N'Pond edge', NULL, 'clay');

INSERT INTO plot.sowing (sowing_id, bed_id, crop, sown, harvested)
VALUES (1, 1, N'Leeks', '2026-03-01', NULL),
       (2, 1, N'Beans', '2026-04-10', '2026-07-20'),
       (3, 3, N'Kale', '2026-05-01', '2026-09-30'),
       (4, 4, N'Mint', '2026-04-01', '2026-08-15');
