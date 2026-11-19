-- A user's preferences, a json column whose schema the application declares, which only the admin module imports.
CREATE PROCEDURE admin.get_prefs @user_id int
AS
SELECT u.user_id, u.prefs FROM admin.users AS u WHERE u.user_id = @user_id;
