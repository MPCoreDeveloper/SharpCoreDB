-- Test query to debug LEFT JOIN with multiple matches
-- Expected results:
-- Order 1: 1 row (payment 1)
-- Order 2: 2 rows (payments 2 and 3)
-- Order 3: 1 row (NULL payment)
-- Total: 4 rows

SELECT o.id as order_id, o.customer_id, p.id as payment_id, p.method
FROM orders o
LEFT JOIN payments p ON p.order_id = o.id
WHERE o.id IN (1, 2, 3)
ORDER BY o.id, p.id;
