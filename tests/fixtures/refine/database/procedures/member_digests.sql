-- Aggregate subqueries that return the outer member's nickname: their one row comes even when WHERE, which tests the
-- nickname, removed every row, so the nickname in it may still be null.
CREATE PROCEDURE lend.member_digests
AS
SET NOCOUNT ON;

SELECT m.member_id,
       (SELECT m.nickname AS nickname, COUNT_BIG(*) AS loans
        FROM lend.loan AS l
        WHERE l.member_id = m.member_id AND m.nickname IS NOT NULL
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS digest,
       json_query((SELECT json_object('nickname': m.nickname, 'loans': COUNT_BIG(*))
                   FROM lend.loan AS l
                   WHERE l.member_id = m.member_id AND m.nickname IS NOT NULL)) AS summary
FROM lend.member AS m
ORDER BY m.member_id;
