\echo 'SharpCoreDB tool compatibility smoke start'
\conninfo

SELECT 1 AS connectivity_check;

SELECT table_name
FROM information_schema.tables
ORDER BY table_name
LIMIT 10;

SELECT column_name, data_type
FROM information_schema.columns
ORDER BY table_name, ordinal_position
LIMIT 20;

SELECT current_database();

\echo 'SharpCoreDB tool compatibility smoke complete'
