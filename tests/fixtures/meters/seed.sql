-- Seed for tests/fixtures/meters. Invented data: two electricity meters and their daily readings. Meter 2 has no site, meter
-- 3 has no readings. No value overflows: under the declared session an overflow is an error, which fails the call, and the
-- IR describes calls that succeed.
INSERT INTO meter.meter (meter_id, label, site)
VALUES (1, 'Barn', 'North field'),
       (2, 'Kiln', NULL),
       (3, 'Shed', 'Yard');

INSERT INTO meter.reading (reading_id, meter_id, read_on, kwh, peak_kw)
VALUES (10, 1, '2026-11-01', 120, 4.250),
       (11, 1, '2026-11-02', 95, 3.875),
       (12, 1, '2026-11-03', 140, 5.125),
       (20, 2, '2026-11-01', 800, 22.500),
       (21, 2, '2026-11-02', 0, 0.000);
