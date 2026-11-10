-- Seed for tests/fixtures/flow. Invented data chosen to reach the edge cases: a sale whose region is gone (so the left
-- join gives nulls), sales with and without notes on every channel, a phone sale noted from its region and one whose
-- region is gone, a region with no sales, a root employee with no title and a deeper chain, an assembly with a
-- sub-assembly, a part with no unit and a part used in nothing.
INSERT INTO flow.region (region_id, code, name)
VALUES (1, 'NTH', N'Northmoor'),
       (2, 'STH', N'Southgate'),
       (3, 'EST', N'Eastwick');

INSERT INTO flow.sale (sale_id, region_id, amount, channel, note)
VALUES (10, 1, 12.50, 'web', NULL),
       (11, 1, 0.00, 'store', N'returned the same day'),
       (12, 2, 99.99, 'phone', NULL),
       (13, 2, 5.00, 'web', N'gift wrap'),
       (14, 9, 7.25, 'phone', N'region since closed'),
       (15, 9, 3.10, 'store', NULL);

INSERT INTO flow.employee (employee_id, manager_id, name, title)
VALUES (1, NULL, N'Wren Calder', NULL),
       (2, 1, N'Ossie Brandt', N'Head of Making'),
       (3, 2, N'Tamsin Rook', NULL),
       (4, 2, N'Ilse Varga', N'Fitter'),
       (5, 1, N'Noor Quill', N'Buyer');

INSERT INTO flow.part (part_id, name, unit)
VALUES (1, N'Garden bench', 'each'),
       (2, N'Seat frame', 'each'),
       (3, N'Slat', 'm'),
       (4, N'Screw', NULL),
       (5, N'Varnish', 'kg'),
       (6, N'Spare leg', 'each');

INSERT INTO flow.component (assembly_id, part_id, quantity)
VALUES (1, 2, 1),
       (1, 5, 2),
       (2, 3, 6),
       (2, 4, 24);
