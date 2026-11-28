CREATE TABLE gate.entry (
    id int IDENTITY(1, 1) NOT NULL PRIMARY KEY,
    ticket int NOT NULL,
    entered_at datetime2(3) NOT NULL
);
