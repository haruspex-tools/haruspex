CREATE TABLE calls.delivery
(
    delivery_id int IDENTITY(1, 1) NOT NULL PRIMARY KEY,
    batch_id int NOT NULL REFERENCES calls.batch (batch_id)
);
