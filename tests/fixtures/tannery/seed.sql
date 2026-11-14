-- Seed for tests/fixtures/tannery. Invented data: vats whose strings are stored in spellings their CHECKs accept as equal to
-- a member, but not the member: another case, trailing spaces, an accent the collation ignores, a soft hyphen (CHAR(173))
-- the collation ignores. Each is read back, and parsed by the generated Zod, as the member it equals.
INSERT INTO dye.vat (vat_id, state, shade, mode, batch, label, grade, tone, finish)
VALUES (1, 'idle', 'teal', 'Hot', 'wool-a', N'warp', 'fine', 'pale', 'matt'),
       (2, 'DYEING', 'ROSE', 'hot', 'SILK.B', N'WEFT', 'COARSE', 'Deep', 'GLOSS'),
       (3, 'Rinse   ', 'tèal', 'cold  ', 'fl' + CHAR(173) + 'ax_c', NULL, NULL, 'café', 'Matt '),
       (4, 'iDlE', 'Jet', NULL, NULL, NULL, NULL, NULL, NULL);

INSERT INTO dye.rinse_tank (tank_id, state)
VALUES (1, 'DRAIN'),
       (2, 'idle ');
