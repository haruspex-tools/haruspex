-- Seed for tests/fixtures/cases. Invented data: a small bicycle repair desk. Customer 3 has no jobs; job 10 has no part
-- lines and job 11 two, one without a price; job 12 one line priced 0. Every branch of every CASE, IIF and CHOOSE is taken:
-- each status, a priority NULL, 1, 2, 3 and 4 (beyond CHOOSE's three), a job done late and ones not, a phone, an empty
-- phone and none, a note, a note row whose note is NULL, and jobs without one.
INSERT INTO desk.customer (customer_id, name, phone, tier)
VALUES (1, N'Ana', '0700 100', 1),
       (2, N'Ben', '', 2),
       (3, N'Cleo', NULL, 3);

INSERT INTO desk.job (job_id, customer_id, status, priority, quoted, promised_on, done_on)
VALUES (10, 1, 'N', NULL, NULL, NULL, NULL),
       (11, 1, 'W', 1, 40.00, '2026-11-05', NULL),
       (12, 2, 'D', 3, 25.50, '2026-11-01', '2026-11-03'),
       (13, 2, 'X', 4, 0.00, '2026-11-02', '2026-11-01'),
       (14, 1, 'D', 2, 12.00, '2026-11-04', '2026-11-04');

INSERT INTO desk.job_note (job_id, note)
VALUES (10, N'Bring the lock'),
       (11, NULL);

INSERT INTO desk.part_line (line_id, job_id, part, qty, price)
VALUES (100, 11, N'Chain', 1, 19.99),
       (101, 11, N'Brake pads', 2, NULL),
       (102, 12, N'Tube', 1, 0.00);
