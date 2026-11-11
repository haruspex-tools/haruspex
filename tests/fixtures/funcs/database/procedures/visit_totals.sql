CREATE PROCEDURE gym.visit_totals
    @member_id int
AS
SELECT m.plan_name,
       STRING_AGG(m.nickname, ', ') WITHIN GROUP (ORDER BY m.member_id) AS nicknames,
       STRING_AGG(m.name, ', ') AS names,
       STRING_AGG(m.member_id, '|') AS ids
FROM gym.member AS m
GROUP BY m.plan_name
ORDER BY m.plan_name;

SELECT STRING_AGG(CAST(v.minutes AS varchar(11)), ',') AS minutes_list,
       STRING_AGG(m.prefs, ';') AS all_prefs,
       STRING_AGG(m.name, ',') AS visitor_names
FROM gym.visit AS v
JOIN gym.member AS m ON m.member_id = v.member_id
WHERE v.member_id = @member_id;
