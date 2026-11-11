-- A row that breaks ck_ticket_late, written before the constraint is added WITH NOCHECK, which leaves existing rows
-- unchecked: the constraint is not trusted, and is not read (no constraint added by ALTER TABLE is). One batch, so
-- the row is in place before the constraint whatever order the deployer runs the scripts in.
INSERT INTO chk.ticket (ticket_id, code, status, title, late) VALUES (5, N'E5', 'held', N'Five', -2147483648);
ALTER TABLE chk.ticket WITH NOCHECK ADD CONSTRAINT ck_ticket_late CHECK (late > 0);
