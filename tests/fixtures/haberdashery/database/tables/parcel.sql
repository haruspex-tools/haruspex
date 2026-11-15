-- A parcel with sparse columns and a column set: SELECT * gives the set, as xml, in place of the sparse columns.
CREATE TABLE shop.parcel (
    parcel_id int NOT NULL CONSTRAINT pk_parcel PRIMARY KEY,
    weight_g int SPARSE NULL,
    label nvarchar(20) NULL,
    fragile bit SPARSE NULL,
    extras xml COLUMN_SET FOR ALL_SPARSE_COLUMNS
);
