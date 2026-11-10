CREATE TABLE rink.booking (
    booking_id int NOT NULL CONSTRAINT pk_booking PRIMARY KEY,
    session_id int NOT NULL CONSTRAINT fk_booking_session REFERENCES rink.session (session_id),
    member_id int NULL CONSTRAINT fk_booking_member REFERENCES rink.member (member_id),
    guest_id int NULL CONSTRAINT fk_booking_guest REFERENCES rink.guest (guest_id),
    skates tinyint NULL,
    extras nvarchar(max) NULL
);
