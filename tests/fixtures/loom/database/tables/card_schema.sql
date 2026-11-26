-- A pattern card, in dbo, which a name without a schema finds outside a module and from a module of a schema without one.
CREATE XML SCHEMA COLLECTION dbo.card_schema AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="card"><xs:complexType><xs:attribute name="weaver" type="xs:string"/></xs:complexType></xs:element></xs:schema>';
