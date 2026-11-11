CREATE TABLE chk.ticket_event
(
    event_id   int       NOT NULL CONSTRAINT pk_ticket_event PRIMARY KEY,
    ticket_id  int       NOT NULL CONSTRAINT fk_ticket_event_ticket REFERENCES chk.ticket (ticket_id),
    kind       nchar(4)  NOT NULL CONSTRAINT ck_ticket_event_kind CHECK (kind IN (N'open', N'note', N'done')),
    minutes    smallint  NULL CONSTRAINT ck_ticket_event_minutes CHECK (minutes > 0)
);
