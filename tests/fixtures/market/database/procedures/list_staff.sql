-- The shop's staff, read from admin's users: rows of the shape admin's procedures return, so it crosses two modules.
CREATE PROCEDURE shop.list_staff
AS
SELECT u.user_id, u.login FROM admin.users AS u WHERE u.retired = 0;
