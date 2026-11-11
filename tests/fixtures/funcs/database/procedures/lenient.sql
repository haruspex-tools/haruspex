CREATE PROCEDURE gym.lenient
AS
-- Run only under the caller's ANSI_WARNINGS and ARITHABORT off (the lenient setup in runs.json), where an overflow is NULL instead of an error: each
-- of these overflows on some rows and not on others. The arithmetic reads only columns never null, so overflow is its only
-- reason to be NULL: room 1 fits and rooms 2 and 3 overflow float and datetime; a fee times room 24 or more overflows money.
SELECT v.visit_id,
       LAG(v.room, 1, 1000) OVER (ORDER BY v.visit_id) AS room_before,
       CAST(v.room AS float) * 1e308 AS heavy,
       v.fee * (v.room * CAST(50000000000000 AS bigint)) AS fee_scaled,
       DATEADD(day, 0, '2026-01-01') + v.room * 1000000 AS far_future
FROM gym.visit AS v
ORDER BY v.visit_id;
