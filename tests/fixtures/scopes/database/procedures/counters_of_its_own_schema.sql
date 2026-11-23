-- lending_counter and counter_code without a schema: lib's, since the procedure and the function are in lib, whoever calls.
CREATE PROCEDURE lib.counters_of_its_own_schema
AS
SELECT c.counter_id, c.label, lib.counter_code_of(c.label) AS code
FROM lending_counter AS c
ORDER BY c.counter_id;
