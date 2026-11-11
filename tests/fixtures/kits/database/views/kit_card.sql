-- A kit with its parts built by FOR JSON and a summary built by json_object: both are text once they leave the view.
CREATE VIEW kit.kit_card
AS
SELECT k.kit_id,
       (SELECT p.sku, p.qty FROM kit.kit_part AS p WHERE p.kit_id = k.kit_id ORDER BY p.part_id FOR JSON PATH) AS parts,
       json_object('name': k.name, 'price': k.price) AS summary
FROM kit.kit AS k;
