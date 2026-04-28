# SharpCoreDB SQL Syntax Reference

This document lists practical SQL syntax supported by SharpCoreDB for daily usage in Viewer.

## Notes

- SharpCoreDB aims for broad SQLite compatibility.
- Prefer parameterized SQL for user input.
- Some advanced syntax is mode-dependent (directory/single-file internals).

## DDL

### CREATE TABLE

```sql
CREATE TABLE users (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    email TEXT,
    created_at DATETIME
);
```

```sql
CREATE TABLE IF NOT EXISTS audit_log (
    id INTEGER PRIMARY KEY,
    action TEXT NOT NULL,
    created_at DATETIME NOT NULL
);
```

### Supported Column Types

- INTEGER
- BIGINT
- TEXT
- REAL
- BLOB
- BOOLEAN
- DATETIME
- LONG
- DECIMAL
- ULID
- GUID
- ROWREF
- VECTOR

### ALTER TABLE

```sql
ALTER TABLE users ADD COLUMN last_login DATETIME;
```

```sql
ALTER TABLE users RENAME TO app_users;
```

### DROP TABLE

```sql
DROP TABLE users;
```

```sql
DROP TABLE IF EXISTS users;
```

### CREATE INDEX

```sql
CREATE INDEX idx_users_email ON users(email);
```

## DML

### INSERT

```sql
INSERT INTO users (id, name, email)
VALUES (1, 'Alice', 'alice@example.com');
```

```sql
INSERT INTO users (id, name, email)
VALUES (2, 'Bob', 'bob@example.com'),
       (3, 'Charlie', 'charlie@example.com');
```

### SELECT

```sql
SELECT id, name, email
FROM users
ORDER BY id DESC
LIMIT 100;
```

```sql
SELECT *
FROM users
WHERE name = 'Alice';
```

```sql
SELECT *
FROM users
WHERE id BETWEEN 10 AND 50;
```

### UPDATE

```sql
UPDATE users
SET email = 'new@example.com'
WHERE id = 1;
```

### DELETE

```sql
DELETE FROM users
WHERE id = 1;
```

## Metadata Queries (SQLite-style)

```sql
SELECT name
FROM sqlite_master
WHERE type = 'table'
ORDER BY name;
```

```sql
PRAGMA table_info(users);
```

## Practical Viewer Tips

- Use `SELECT ... LIMIT N` for large tables.
- Use Preview mode for paging quickly.
- For `ROWREF`, define matching `FOREIGN KEY` constraints when required by your schema rules.

## Troubleshooting

### Invalid CREATE TABLE syntax

- Ensure table and column names are valid identifiers (`A-Z`, `a-z`, `0-9`, `_`, and not starting with a digit).
- Avoid mismatched parentheses.
- Ensure each column has a type.

### Connection/path issues

- Single-file databases should use `.scdb`.
- Directory mode expects a folder path, not a file path.

---

Last updated: 2026-04-27
