CREATE TABLE flow.region (
    region_id int NOT NULL PRIMARY KEY,
    code char(3) NOT NULL CONSTRAINT ck_region_code CHECK (code IN ('NTH', 'STH', 'EST', 'WST')),
    name nvarchar(30) NOT NULL
);
