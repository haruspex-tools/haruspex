CREATE PROCEDURE tailor.customer_summary
AS
SELECT name,
       band + 1 AS next_band,
       '[' + size_code + ']' AS size_label,
       CAST(size_code AS char(3)) AS size_cast,
       ISNULL(phone, 'none') AS phone_or_none,
       LEN(email) AS email_length,
       CASE WHEN is_trade = 1 THEN 'trade' ELSE 'retail' END AS kind,
       CASE WHEN band = 1 THEN size_code ELSE 'M10' END AS size_or_m10,
       COUNT(born) OVER () AS with_birthday,
       tailor.size_of(NULLIF(customer_id, 3)) AS size_by_function
FROM tailor.customer;
