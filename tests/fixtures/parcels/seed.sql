-- The parcels fixture's seed: options with every key, with only the required ones, with a null note, and none (a NULL column);
-- routes of one stop and of both kinds; extra as an object, an array, and NULL.
INSERT INTO parcels.parcel (parcel_id, label, options, route, extra) VALUES
    (1, N'Bicycle wheel', N'{"speed":"express","signature":true,"note":"Leave with the porter","window":{"from":"09:00:00","to":"12:00:00"},"attempts":2}',
        '[{"kind":"depot","depot":"North"},{"kind":"address","postcode":"AB1 2CD","line":"1 Mill Lane"}]', N'{"scanner":"S-4","tags":["fragile"]}'),
    (2, N'Tea chest', N'{"speed":"standard","signature":false}', '[{"kind":"address","postcode":"EF3 4GH"}]', N'[1,"two",null]'),
    (3, N'Umbrella stand', N'{"speed":"standard","signature":false,"note":null}', '[{"kind":"depot","depot":"South"}]', NULL),
    (4, N'Kite', NULL, '[{"kind":"depot","depot":"North"}]', N'{}');
