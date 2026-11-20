-- Seed for tests/fixtures/beads. Invented data chosen to reach each case the assembled arrays allow: an order with lines
-- and tags, among them a tag holding a quotation mark and a backslash, which string_escape keeps one string; an order with
-- neither, whose aggregates are NULL; and each channel, none included.
--
-- order 1: three lines (a gift among them), three tags, sold through both channels.
-- order 2: no lines, no tags, no channel.
-- order 3: one line, one tag, retail only.
-- order 4: no lines, no tags, trade only.
INSERT INTO bead.[order] (order_id, ref, channel)
VALUES (1, N'BD-0001', 'both'),
       (2, N'BD-0002', NULL),
       (3, N'BD-0003', 'retail'),
       (4, N'BD-0004', 'trade');

INSERT INTO bead.order_line (line_id, order_id, qty, price, gift)
VALUES (10, 1, 2, 0.50, 0),
       (11, 1, 1, 12.00, 1),
       (12, 1, 30, -1.25, 0),
       (20, 3, 5, 99999.99, 1);

INSERT INTO bead.order_tag (order_id, tag)
VALUES (1, N'glass'),
       (1, N'say "hi"'),
       (1, N'a\b'),
       (3, N'wooden');
