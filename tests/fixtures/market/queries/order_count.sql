SELECT COUNT_BIG(*) AS orders FROM sales.orders AS o WHERE o.placed >= @since;
