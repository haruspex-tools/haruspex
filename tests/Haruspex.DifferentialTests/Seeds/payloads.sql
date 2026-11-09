-- Seed for tests/fixtures/payloads. Invented data chosen to reach every case the constructors' IR allows: keys at
-- int's edges, strings of their full length, prices and stars and weights at their bounds, a null in every nullable
-- value, an author with no books, books with no reviews and no tags, and groups of exactly one row (an aggregate's
-- fewest items).
--
-- author -2147483648: two books (the extremes), every value at its longest.
-- author  2147483647: one book, nulls.
-- author           3: no books.
-- author           4: one book with no reviews and no tags.
INSERT INTO pay.author (author_id, name, bio, profile)
VALUES (-2147483648, REPLICATE(N'a', 40), REPLICATE(N'b', 199) + NCHAR(10), N'{"links":["https://example.invalid/a"],"since":1999}'),
       (2147483647, N'Short Name', NULL, NULL),
       (3, N'No Books Yet', N'Writes "slowly".', N'[]'),
       (4, N'Quiet Author', NULL, N'{}');

INSERT INTO pay.book (book_id, author_id, title, isbn, price, published)
VALUES (-2147483648, -2147483648, REPLICATE(N't', 59) + N'é', '9780000000001', 9999999.99, '2001-02-03'),
       (2147483647, -2147483648, N'Thin', NULL, 0.00, NULL),
       (5, 2147483647, N'Middle', '9780000000002', 12.50, '2020-05-06'),
       (6, 4, N'Unreviewed', '9780000000003', 1.00, '2010-01-01');

-- One review per reviewed book, so that each group has exactly one row. The latest review of the book at
-- -2147483648 has 1 star and that of the book at 2147483647 has 5.
INSERT INTO pay.review (review_id, book_id, stars, comment, posted)
VALUES (-2147483648, -2147483648, 1, REPLICATE(N'c', 98) + N'"' + NCHAR(10), '2026-01-02T03:04:05+00:00'),
       (2147483647, 2147483647, 5, NULL, '2026-02-03T04:05:06-05:30'),
       (7, 5, 3, N'Fine.', '2026-03-04T05:06:07+01:00');

-- Tag names are keys, which are data; weights are values, at their bounds and null.
INSERT INTO pay.book_tag (book_id, tag, weight)
VALUES (-2147483648, 'classic', 100),
       (-2147483648, 'tag-of-twenty-chars!', 1),
       (-2147483648, 'unweighted', NULL),
       (5, 'middle', 50);
