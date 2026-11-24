-- A tailor's customers, each masked column of a type each masking function takes. A caller without UNMASK reads the masks:
-- MaskingObservationTests observes each.
CREATE SCHEMA tailor;
GO
CREATE TABLE tailor.customer
(
    customer_id int NOT NULL PRIMARY KEY,
    name nvarchar(40) NOT NULL,
    email varchar(60) MASKED WITH (FUNCTION = 'email()') NOT NULL,
    phone varchar(20) MASKED WITH (FUNCTION = 'partial(0, "XXX-XXX-", 4)') NULL,
    band tinyint MASKED WITH (FUNCTION = 'random(1, 3)') NOT NULL CHECK (band BETWEEN 1 AND 3),
    size_code char(3) MASKED WITH (FUNCTION = 'default()') NOT NULL CHECK (size_code IN ('S08', 'M10', 'L12')),
    chest_cm decimal(5, 1) MASKED WITH (FUNCTION = 'default()') NOT NULL CHECK (chest_cm BETWEEN 60 AND 160),
    born date MASKED WITH (FUNCTION = 'default()') NULL,
    joined datetime2(0) MASKED WITH (FUNCTION = 'datetime("Y")') NOT NULL,
    swatch binary(4) MASKED WITH (FUNCTION = 'default()') NULL,
    is_trade bit MASKED WITH (FUNCTION = 'default()') NOT NULL CHECK (is_trade = 1),
    note nvarchar(max) MASKED WITH (FUNCTION = 'default()') NULL
);
