-- A pressing of the orchard's apples.
CREATE TABLE grove.pressing (
    pressing_id int NOT NULL CONSTRAINT pk_pressing PRIMARY KEY,
    pressed_on date NOT NULL,
    barrel_id int NULL
);
GO
-- With QUOTED_IDENTIFIER off, as press is built: "pressed" is a string.
CREATE PROCEDURE grove.pressings AS
SELECT p.pressing_id, "pressed" AS state FROM grove.pressing AS p;
