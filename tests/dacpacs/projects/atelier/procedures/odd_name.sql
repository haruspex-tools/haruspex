-- A name with a colon and a slash, which a location must quote.
CREATE PROCEDURE [studio].[get:kiln/one] @kiln_id int AS
SELECT k.kiln_id, k.label, k.peak_f FROM studio.kiln AS k WHERE k.kiln_id = @kiln_id;
