-- Seed for tests/fixtures/assays. Invented data: two samples. The first has places past every target and a count with 28
-- integer digits, so that its rounding shows under NUMERIC_ROUNDABORT on; the second has only zeros past two places, which
-- rounds all the same. No value overflows: under the driver's own session an overflow is an error, which fails the call.
INSERT INTO assay.sample (sample_id, concentration, dilution, reference, fee, absorbance, lab_value, ratio, long_ratio, fine_scale, big_count)
VALUES (1, 1.2345, 2.50, 3, 12.3456, 0.4321, '12.3456', 1.0000000001, 1.0000000001, 0.00000000000001, 9999999999999999999999999999.9999999),
       (2, 0.5000, 1.00, 7, 3.0000, 1.5, '0.5', 2.5, 10, 0.5, 1);
