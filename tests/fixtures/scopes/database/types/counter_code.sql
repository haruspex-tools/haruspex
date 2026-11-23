-- An alias type of one name in lib and in dbo, of two lengths: a function of lib returns lib's (lib.counter_code_of).
CREATE TYPE lib.counter_code FROM varchar(6);
GO

CREATE TYPE dbo.counter_code FROM varchar(2);
