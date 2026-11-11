CREATE PROCEDURE gym.visit_money
AS
SELECT v.visit_id,
       v.fee + v.tip AS paid,
       v.fee * 2 AS double_fee,
       v.fee / v.room AS fee_per_room,
       v.tip * v.room AS tip_per_room,
       v.fee + 1.5 AS fee_plus,
       v.weight * v.minutes AS effort,
       v.rating + 1 AS rating_up,
       v.rating * v.weight AS mixed,
       -v.fee AS refund,
       -v.weight AS weight_down,
       -v.room AS room_down,
       -1.5e0 AS float_literal,
       $2.50 AS money_literal,
       m.last_seen + 1 AS seen_next_day,
       m.born + 0.5 AS born_noon,
       m.last_seen - m.born AS seen_since_born,
       COALESCE(v.rating, v.weight) AS some_score,
       COALESCE(v.tip, v.room) AS some_tip,
       COALESCE(m.last_seen, v.started_at) AS some_time,
       COALESCE(m.born, m.last_seen) AS some_datetime
FROM gym.visit AS v
JOIN gym.member AS m ON m.member_id = v.member_id
ORDER BY v.visit_id;
