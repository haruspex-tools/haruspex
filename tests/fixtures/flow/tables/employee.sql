CREATE TABLE flow.employee (
    employee_id int NOT NULL PRIMARY KEY,
    manager_id int NULL REFERENCES flow.employee (employee_id),
    name nvarchar(30) NOT NULL,
    title nvarchar(30) NULL
);
