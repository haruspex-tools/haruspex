-- Columns no row a client sends can fill: an identity and a computed column.
CREATE TYPE till.serial_list AS TABLE (
    serial int IDENTITY(1, 1) PRIMARY KEY,
    label nvarchar(20) NOT NULL,
    shout AS upper(label)
);
