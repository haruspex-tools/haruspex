CREATE PROCEDURE gym.member_names
AS
SELECT m.member_id,
       LTRIM(m.code) AS code_left,
       RTRIM(m.code) AS code_trimmed,
       TRIM(m.nickname) AS nickname_trimmed,
       TRIM('x' FROM m.name) AS name_unx,
       UPPER(m.code) AS code_upper,
       LOWER(m.nickname) AS nickname_lower,
       LOWER(m.plan_name) AS plan_lower,
       LEFT(m.name, 3) AS name_start,
       RIGHT(m.nickname, 2) AS nickname_end,
       SUBSTRING(m.name, 4, 10) AS name_rest,
       LEFT(m.prefs, 5) AS prefs_start,
       LEN(m.code) AS code_length,
       LEN(m.prefs) AS prefs_length,
       DATALENGTH(m.nickname) AS nickname_bytes,
       DATALENGTH(m.joined) AS joined_bytes,
       REPLACE(m.name, 'a', 'aa') AS name_doubled,
       REPLACE(m.nickname, N'o', N'0') AS nickname_zeroed,
       STRING_ESCAPE(m.name, 'json') AS name_escaped,
       FORMATMESSAGE('%s (%s)', m.name, m.nickname) AS label,
       JSON_VALUE(m.prefs, '$.locker') AS locker
FROM gym.member AS m
ORDER BY m.member_id;

SELECT m.member_id AS id,
       UPPER(m.name) AS shout,
       RIGHT(m.nickname, 3) AS tail,
       JSON_VALUE(m.prefs, '$.locker') AS locker,
       LEN(m.nickname) AS nickname_length
FROM gym.member AS m
ORDER BY m.member_id
FOR JSON PATH;
