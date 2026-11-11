CREATE PROCEDURE gym.visit_windows
AS
SELECT v.visit_id,
       ROW_NUMBER() OVER (PARTITION BY v.member_id ORDER BY v.visit_id) AS nth_visit,
       RANK() OVER (ORDER BY v.room) AS room_rank,
       DENSE_RANK() OVER (ORDER BY v.room) AS room_dense_rank,
       NTILE(3) OVER (ORDER BY v.visit_id) AS third,
       COUNT(*) OVER (PARTITION BY v.member_id) AS member_visits,
       COUNT_BIG(v.minutes) OVER (PARTITION BY v.member_id) AS timed_visits,
       SUM(v.minutes) OVER (PARTITION BY v.member_id) AS member_minutes,
       SUM(v.room) OVER (PARTITION BY v.member_id ORDER BY v.visit_id ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING) AS rooms_before,
       SUM(v.fee) OVER (ORDER BY v.visit_id) AS running_fees,
       MAX(v.fee) OVER (PARTITION BY v.member_id ORDER BY v.visit_id ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING) AS dearest_later,
       MIN(v.started_at) OVER (PARTITION BY v.member_id) AS first_started,
       MAX(v.tip) OVER (PARTITION BY v.member_id) AS best_tip,
       FIRST_VALUE(v.minutes) OVER (PARTITION BY v.member_id ORDER BY v.visit_id) AS first_minutes,
       LAG(v.started_at) OVER (PARTITION BY v.member_id ORDER BY v.visit_id) AS previous_started,
       LEAD(v.minutes) OVER (PARTITION BY v.member_id ORDER BY v.visit_id) AS next_minutes,
       LAG(v.fee, 1, 0) OVER (PARTITION BY v.member_id ORDER BY v.visit_id) AS previous_fee,
       LEAD(v.room, 2, 0) OVER (ORDER BY v.visit_id) AS room_after_next,
       LAG(v.room, 0) OVER (ORDER BY v.visit_id) AS same_room,
       FIRST_VALUE(v.room) OVER (PARTITION BY v.member_id ORDER BY v.visit_id) AS first_room,
       LAST_VALUE(v.room) OVER (PARTITION BY v.member_id ORDER BY v.visit_id ROWS BETWEEN 1 FOLLOWING AND 1 FOLLOWING) AS next_room,
       PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY v.minutes) OVER (PARTITION BY v.member_id) AS median_minutes,
       PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY v.room) OVER (PARTITION BY v.member_id) AS median_room,
       PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY v.minutes) OVER (PARTITION BY v.member_id) AS median_minutes_taken
FROM gym.visit AS v
ORDER BY v.visit_id;
