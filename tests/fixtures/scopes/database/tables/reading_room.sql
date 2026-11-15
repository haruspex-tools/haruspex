-- A schema and a table whose names contain dots, which only brackets can write: lib.reading_rooms names its columns by both.
CREATE SCHEMA [lib.annex];
GO
CREATE TABLE [lib.annex].[reading.room]
(
    room_id int          NOT NULL PRIMARY KEY,
    seats   tinyint      NOT NULL,
    note    nvarchar(40) NULL
);
