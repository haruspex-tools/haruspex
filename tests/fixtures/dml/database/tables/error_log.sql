CREATE TABLE depot.error_log
(
    error_id int IDENTITY(1, 1) NOT NULL PRIMARY KEY,
    procedure_name sysname NULL,
    message nvarchar(4000) NULL,
    logged_at datetime2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
