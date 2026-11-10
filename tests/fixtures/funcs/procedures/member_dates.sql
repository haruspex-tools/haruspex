CREATE PROCEDURE gym.member_dates
    @offset int = NULL
AS
SELECT m.member_id,
       DATEDIFF(day, m.joined, m.last_seen) AS days_member,
       DATEDIFF_BIG(minute, m.born, m.joined) AS minutes_alive,
       DATEDIFF(day, 0, m.joined) AS day_number,
       DATEADD(month, 1, m.joined) AS renews_on,
       DATEADD(hour, 2, m.last_seen) AS seen_later,
       DATEADD(year, 18, m.born) AS adult_on,
       DATEADD(day, @offset, m.joined) AS shifted,
       DATEADD(day, 1, '2026-01-01') AS from_text,
       DATEPART(quarter, m.joined) AS joined_quarter,
       YEAR(m.joined) AS joined_year,
       MONTH(m.last_seen) AS seen_month,
       DAY(m.born) AS born_day,
       DATENAME(weekday, m.joined) AS joined_weekday,
       EOMONTH(m.joined) AS month_end,
       EOMONTH(m.joined, @offset) AS month_end_shifted,
       EOMONTH(m.last_seen) AS seen_month_end,
       DATEFROMPARTS(YEAR(m.joined), 1, 1) AS year_start,
       DATEFROMPARTS(YEAR(m.joined), @offset, 1) AS offset_month,
       DATETIME2FROMPARTS(2026, 11, 10, 9, 30, 0, 5, 2) AS opening,
       TIMEFROMPARTS(MONTH(m.born), 0, 0, 0, 0) AS born_hour,
       SYSDATETIME() AS now_local,
       GETDATE() AS now_old,
       GETUTCDATE() AS now_utc_old,
       SYSUTCDATETIME() AS now_utc,
       SYSDATETIMEOFFSET() AS now_offset,
       CURRENT_TIMESTAMP AS now_standard
FROM gym.member AS m
ORDER BY m.member_id;
