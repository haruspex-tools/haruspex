CREATE TABLE admin.users (
    user_id int NOT NULL PRIMARY KEY,
    login varchar(30) NOT NULL,
    retired bit NOT NULL,
    prefs json NULL);
