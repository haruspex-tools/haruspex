-- A lot sold at auction: a hammer price of up to 28 integer digits and 10 places.
CREATE TABLE bazaar.auction_lot (
    lot_id int NOT NULL CONSTRAINT pk_auction_lot PRIMARY KEY,
    closed_at datetime NOT NULL,
    bids tinyint NOT NULL,
    hammer decimal(38,10) NOT NULL,
    reserve decimal(30,0) NOT NULL,
    tier decimal(38,0) NOT NULL CONSTRAINT ck_auction_lot_tier CHECK (tier IN (2, 3)),
    premium_rate real NOT NULL,
    paid_by char(4) NOT NULL
);
