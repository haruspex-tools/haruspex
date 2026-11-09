CREATE TABLE hr.salesperson
(
    salesperson_id  int            NOT NULL PRIMARY KEY,
    display_name    nvarchar(100)  NOT NULL,
    hired_on        date           NOT NULL
);
