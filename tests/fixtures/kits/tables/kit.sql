CREATE TABLE kit.kit
(
    kit_id  int            NOT NULL PRIMARY KEY,
    name    nvarchar(40)   NOT NULL,
    price   decimal(9, 2)  NOT NULL,
    -- JSON stored in a column: someone else may write it, so it is never traced.
    extras  nvarchar(max)  NULL
);
