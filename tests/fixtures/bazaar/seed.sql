-- Seed for tests/fixtures/bazaar. Invented data: a weekend market's sales of four kinds. Each kind has a row at the extremes
-- the view's widened types must hold (the least and greatest dates, a hammer price of 28 nines and 10 places that rounds to
-- 29 integer digits beside decimal(30,0), a 200-character note, a long memo, bigint's ends) and an ordinary one; each nullable
-- column is NULL at least once. The default procedures cannot overflow on it. bazaar.lenient reads what does, under the
-- caller's options: a barter of 3,000,000,000 units (past smallmoney), an estimate of 1e300 (past datetime), a stall sale on
-- 0001-01-01 (before smalldatetime) and a last stall ticket of '3000000000' (past int). A barter of 3 units matches a stall
-- sale of 3, so bazaar.mixed's INTERSECT has a row.
INSERT INTO bazaar.stall_sale (sale_id, sold_on, qty, unit_price, paid_by, share, discount, ticket, note)
VALUES (1, '2026-05-02', 3, 4.50, 'cash', 0.1234567890123456789012345678901234567, 0.25, '1001', N'Three jars of plum jam');
INSERT INTO bazaar.stall_sale (sale_id, sold_on, qty, unit_price, paid_by, share, discount, ticket, note)
VALUES (2, '0001-01-01', 1, 0.00, 'card', -9.9999999999999999999999999999999999999, 0.75, '1002', NULL);
INSERT INTO bazaar.stall_sale (sale_id, sold_on, qty, unit_price, paid_by, share, discount, ticket, note)
VALUES (3, '9999-12-31', 100, 9999999.99, 'card', 9.9999999999999999999999999999999999999, 0.25, '3000000000', REPLICATE('n', 200));

INSERT INTO bazaar.web_order (order_id, placed_at, qty, price, channel, voucher, ref_code)
VALUES (1, '2026-05-02 09:15:00.125 +01:00', 2, 12.99, N'app', '6F9619FF-8B86-D011-B42D-00C04FC964FF', '0E984725-C51C-4BF4-9960-E1C80E27ABA0'),
       (2, '0001-01-01 00:00:00.000 -14:00', 1, 0.0001, N'site', NULL, '1D2C3B4A-0000-4000-8000-000000000001'),
       (3, '9999-12-31 23:59:59.999 +14:00', 1, 922337203685477.5807, N'site', NULL, 'FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF');

INSERT INTO bazaar.auction_lot (lot_id, closed_at, bids, hammer, reserve, tier, premium_rate, paid_by)
VALUES (1, '2026-05-02 16:00', 12, 1250.5, 1000, 2, 0.125, 'bank');
INSERT INTO bazaar.auction_lot (lot_id, closed_at, bids, hammer, reserve, tier, premium_rate, paid_by)
VALUES (2, '1753-01-01', 255, 9999999999999999999999999999.9999999999, 999999999999999999999999999999, 3, 3.4e38, 'cash');
INSERT INTO bazaar.auction_lot (lot_id, closed_at, bids, hammer, reserve, tier, premium_rate, paid_by)
VALUES (3, '9999-12-31 23:59:59.997', 0, -9999999999999999999999999999.9999999999, -999999999999999999999999999999, 2, -3.4e38, 'card');

INSERT INTO bazaar.barter (barter_id, swapped_at, opened, units, estimate, small_fee, memo)
VALUES (1, '2026-05-02 11:30', '10:00', 6, 40.5, 1.50, N'Six eggs for a loaf'),
       (2, '1900-01-01', '00:00', 9223372036854775807, 1e300, 214748.3647, REPLICATE(CAST(N'm' AS nvarchar(max)), 9000)),
       (3, '2079-06-06 23:59', '23:59:59.9999999', -9223372036854775808, -2.5, -214748.3648, NULL),
       (4, '2026-05-03 12:00', '12:00', 3000000000, 2.5, 0, NULL),
       (5, '2026-05-03 12:30', '12:30', 3, 7.25, 0.75, N'Three jars for a chair');
