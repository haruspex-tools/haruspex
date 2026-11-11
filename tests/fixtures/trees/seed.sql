-- Seed for tests/fixtures/trees. Invented data: a bill of materials several levels deep, and a catalogue of categories
-- whose contents hold further categories. Chosen so that every case the IR allows is reached at some depth (a recursive
-- type's cases are counted by its name, so any depth counts), with keys at int's edges inside the trees as well as at
-- their roots, and strings at their full length.
--
-- Bill of materials (assemblies in capitals), quantities in brackets:
--   TRAILER KIT 1
--     WHEEL ASSEMBLY 10 (2)
--       rim 20 (1), spoke 21 (1000), HUB ASSEMBLY 22 (1), bolt 2147483647 (4)
--         HUB ASSEMBLY 22: bearing -2147483648 (2), bolt 2147483647 (1)
--     FRAME ASSEMBLY 11 (1): bolt 2147483647 (6)
--     SPARE KIT 12 (1): an assembly with no components yet
-- The bolt is used in three assemblies; the deepest leaf, the bearing, is four levels down.
INSERT INTO bom.part (part_id, name, kind, unit_cost)
VALUES (1, N'Trailer kit', 'assembly', NULL),
       (10, N'Wheel assembly', 'assembly', NULL),
       (11, N'Frame assembly', 'assembly', NULL),
       (12, N'Spare kit', 'assembly', NULL),
       (20, N'Rim, cast', 'part', 9999999.99),
       (21, N'Spoke ' + REPLICATE(N'x', 23) + N'é', 'part', 0.00),
       (22, N'Hub assembly', 'assembly', NULL),
       (-2147483648, N'Bearing', 'part', 3.75),
       (2147483647, N'Bolt M6', 'part', 0.05);

INSERT INTO bom.component (assembly_id, part_id, quantity)
VALUES (1, 10, 2),
       (1, 11, 1),
       (1, 12, 1),
       (10, 20, 1),
       (10, 21, 1000),
       (10, 22, 1),
       (10, 2147483647, 4),
       (22, -2147483648, 2),
       (22, 2147483647, 1),
       (11, 2147483647, 6);

-- Catalogue:
--   Garden 5: items 7, 8
--     Tools -2147483648: no items
--       Hand tools 2: no items
--         Trowels 2147483647: item 2147483647
--     Seeds and bulbs 3 (a name at its full length): item -2147483648
--   Empty 4: nothing at all
INSERT INTO cat.category (category_id, parent_id, name)
VALUES (5, NULL, N'Garden'),
       (-2147483648, 5, N'Tools'),
       (2, -2147483648, N'Hand tools'),
       (2147483647, 2, N'Trowels'),
       (3, 5, REPLICATE(N's', 19) + N'é'),
       (4, NULL, N'Empty');

-- Items relate to others, or to none (a card of NULL); the two at int's edges have notes, one at its full length.
INSERT INTO cat.item (item_id, category_id, title, note, related_item_id)
VALUES (-2147483648, 3, REPLICATE(N't', 19) + N'é', REPLICATE(N'n', 20), NULL),
       (2147483647, 2147483647, N'Trowel', N'Steel', -2147483648),
       (7, 5, N'Hose', NULL, 2147483647),
       (8, 5, N'Gloves', NULL, 7);
