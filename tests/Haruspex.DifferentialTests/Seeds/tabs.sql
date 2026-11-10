-- Seed for tests/fixtures/tabs. Invented data: a small café's tabs. Guest 3 has no tabs; tab 13 has no items; tab 11's note
-- is not a number and tab 12 has none; one menu item has no short code. Every nullable value is null in some row returned:
-- a nickname, a phone, a closing time, a table number, a tip, a note, a discount and a short code.
INSERT INTO cafe.guest (guest_id, name, nickname, phone)
VALUES (1, N'Ada', N'Ace', '0123 456'),
       (2, N'Bo', NULL, NULL),
       (3, N'Cy', N'Cyc', NULL);

INSERT INTO cafe.menu_item (item_id, title, price, short_code)
VALUES (100, N'Flat white', 3.25, 'FLAT'),
       (101, N'Toast', 2.50, NULL),
       (102, N'Soup', 6.75, 'SOUP');

INSERT INTO cafe.tab (tab_id, guest_id, opened_at, closed_at, table_no, tip, note)
VALUES (10, 1, '2026-11-01T08:00:00', '2026-11-01T09:00:00', 4, 1.50, '12'),
       (11, 1, '2026-11-02T08:00:00', NULL, NULL, NULL, 'abc'),
       (12, 2, '2026-11-03T12:00:00', NULL, 2, NULL, NULL),
       (13, 2, '2026-11-04T12:00:00', '2026-11-04T13:00:00', NULL, NULL, NULL);

INSERT INTO cafe.tab_item (tab_item_id, tab_id, item_id, qty, discount)
VALUES (1000, 10, 100, 2, NULL),
       (1001, 10, 101, 1, 0.50),
       (1002, 11, 102, 1, NULL),
       (1003, 12, 101, 3, NULL);
