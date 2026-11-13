-- Seed for tests/fixtures/kennel. Invented data: a small boarding kennel. Owner 2 has no phone; dog 3 has no chip and dog 4
-- no owner; dog 1 is booked and staying, dog 2 booked and not staying, dog 5 staying and not booked, so a MERGE meets each
-- of its actions; stay 2 has no note.
INSERT INTO kennel.owner (owner_id, name, phone)
VALUES (1, N'Ana', '555-0101'),
       (2, N'Ben', NULL),
       (3, N'Cleo', '555-0103');

INSERT INTO kennel.dog (dog_id, name, chip, owner_id)
VALUES (1, N'Biscuit', 'C-0001', 1),
       (2, N'Pepper', 'C-0002', 1),
       (3, N'Moss', NULL, 2),
       (4, N'Rook', 'C-0004', NULL),
       (5, N'Juno', 'C-0005', 1);

INSERT INTO kennel.stay (dog_id, nights, note)
VALUES (1, 2, N'Feeds twice a day'),
       (5, 3, NULL),
       (3, 1, N'Shy');

INSERT INTO kennel.booking (dog_id, nights)
VALUES (1, 4),
       (2, 5),
       (3, 2);
