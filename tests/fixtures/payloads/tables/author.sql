-- profile is the native json type (SQL Server 2025): live JSON whose shape is in the data.
CREATE TABLE pay.author
(
    author_id int          NOT NULL PRIMARY KEY,
    name      nvarchar(40) NOT NULL,
    bio       nvarchar(200) NULL,
    profile   json         NULL
);
GO
