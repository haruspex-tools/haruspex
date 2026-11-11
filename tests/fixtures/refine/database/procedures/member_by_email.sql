-- A lookup by a unique key that is not the primary key: at most one row, and the email compared is never null in it.
CREATE PROCEDURE lend.member_by_email
    @email varchar(80)
AS
SET NOCOUNT ON;

SELECT m.member_id, m.name, m.email, m.nickname
FROM lend.member AS m
WHERE m.email = @email;
