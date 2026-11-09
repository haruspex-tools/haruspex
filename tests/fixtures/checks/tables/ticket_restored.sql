-- Disabled, then enabled WITH CHECK, which validates every row and fails if one breaks it: in the same script, so in
-- this order, and afterwards the constraint holds again and is read.
ALTER TABLE chk.ticket NOCHECK CONSTRAINT ck_ticket_restored;
GO
ALTER TABLE chk.ticket WITH CHECK CHECK CONSTRAINT ck_ticket_restored;
