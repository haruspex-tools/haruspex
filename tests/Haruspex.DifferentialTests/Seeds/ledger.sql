-- Seed for tests/fixtures/ledger. Invented data: a small ledger of accounts and their lines. Account 1 has three ordinary lines,
-- one with every nullable input NULL, a price of 0 and no units; account 2 has one line at every column's greatest value and
-- account 3 one at every least value (38 digits of 9 in big and frac, the ends of money, smallmoney, int, smallint, bigint and
-- tinyint, and real within 1e31 of its ends, since a float literal past real's greatest value does not convert); account 4 has
-- two lines whose rates, discounts, scores and deltas are all NULL; account 5 has none. weight stays moderate: a float's
-- squares at its extremes overflow VAR, which NumericObservationTests shows. Each nullable input is NULL at least once. No
-- default run overflows; ledger.lenient overflows on the extremes, divides by the zero units and reads the zero and negative
-- prices.
INSERT INTO ledger.account (account_id, name)
VALUES (1, N'Alder'), (2, N'Birch'), (3, N'Cedar'), (4, N'Dune'), (5, N'Elm');

-- One INSERT a line: a VALUES list of several rows types each column by all its literals, which past 38 digits overflows.
INSERT INTO ledger.line (line_id, account_id, qty, unit_price, rate, discount, big, frac, fee, small_fee, weight, score, units, flag, delta, ref_no)
VALUES (1, 1, 3, 12.50, 0.250000, 0.1000, 1000, 0.5, 10.00, 1.50, 2.5, 1.5, 4, 1, 7, 100);
INSERT INTO ledger.line (line_id, account_id, qty, unit_price, rate, discount, big, frac, fee, small_fee, weight, score, units, flag, delta, ref_no)
VALUES (2, 1, 5, 0.00, NULL, NULL, -42, -0.25, 0.00, NULL, NULL, NULL, 0, 0, NULL, NULL);
INSERT INTO ledger.line (line_id, account_id, qty, unit_price, rate, discount, big, frac, fee, small_fee, weight, score, units, flag, delta, ref_no)
VALUES (3, 1, -2, -7.25, 1.500000, 0.0500, 7, 0.125, -3.00, -2.25, -1.25, -0.5, 9, 1, -3, -5);
INSERT INTO ledger.line (line_id, account_id, qty, unit_price, rate, discount, big, frac, fee, small_fee, weight, score, units, flag, delta, ref_no)
VALUES (4, 2, 2147483647, 99999999999999999.99, 9999999999999.999999, 9.9999, 99999999999999999999999999999999999999,
           0.99999999999999999999999999999999999999, 922337203685477.5807, 214748.3647, 1e10, 3.4028234e38, 255, 1, 32767, 9223372036854775807);
INSERT INTO ledger.line (line_id, account_id, qty, unit_price, rate, discount, big, frac, fee, small_fee, weight, score, units, flag, delta, ref_no)
VALUES (5, 3, -2147483648, -99999999999999999.99, -9999999999999.999999, -9.9999, -99999999999999999999999999999999999999,
           -0.99999999999999999999999999999999999999, -922337203685477.5808, -214748.3648, -1e10, -3.4028234e38, 0, 0, -32768, -9223372036854775808);
INSERT INTO ledger.line (line_id, account_id, qty, unit_price, rate, discount, big, frac, fee, small_fee, weight, score, units, flag, delta, ref_no)
VALUES (6, 4, 1, 1.00, NULL, NULL, 1, 0, 1.00, NULL, 3, NULL, 1, 0, NULL, NULL);
INSERT INTO ledger.line (line_id, account_id, qty, unit_price, rate, discount, big, frac, fee, small_fee, weight, score, units, flag, delta, ref_no)
VALUES (7, 4, 2, 2.00, NULL, NULL, 2, 0.1, 2.00, 0.10, 4, NULL, 2, 1, NULL, 1);
