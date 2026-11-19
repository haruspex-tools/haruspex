CREATE PROCEDURE admin.list_retired_users
AS
SELECT u.user_id, u.login FROM admin.users AS u WHERE u.retired = 1;
