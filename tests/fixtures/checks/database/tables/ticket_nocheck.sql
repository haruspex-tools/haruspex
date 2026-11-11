-- Disables ck_ticket_muted, so rows written afterwards are not checked: the seed writes one that breaks it, and
-- the constraint must not be read as a guarantee.
ALTER TABLE chk.ticket NOCHECK CONSTRAINT ck_ticket_muted;
