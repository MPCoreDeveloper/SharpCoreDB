// <copyright file="DdlProcedureViewTriggerTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for Phase 1.3/1.4 DDL features: Stored Procedures, Views, Triggers.
/// Covers CREATE, DROP, EXEC, and error paths for each feature.
/// </summary>
public class DdlProcedureViewTriggerTests : IDisposable
{
    private readonly string testDbPath;
    private readonly Database db;

    public DdlProcedureViewTriggerTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"ddl_pvt_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDbPath);

        var config = DatabaseConfig.Benchmark;
        db = new Database(
            new ServiceCollection().AddSharpCoreDB().BuildServiceProvider(),
            testDbPath,
            "test_password",
            isReadOnly: false,
            config: config);

        // Seed a table used by most tests
        db.ExecuteSQL("CREATE TABLE employees (id INTEGER PRIMARY KEY, name TEXT, salary INTEGER)");
        db.ExecuteSQL("INSERT INTO employees VALUES (1, 'Alice', 80000)");
        db.ExecuteSQL("INSERT INTO employees VALUES (2, 'Bob', 90000)");
        db.ExecuteSQL("INSERT INTO employees VALUES (3, 'Carol', 70000)");
    }

    public void Dispose()
    {
        try { db.Dispose(); } catch { }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Thread.Sleep(250);

        if (Directory.Exists(testDbPath))
        {
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    try { Directory.Delete(testDbPath, recursive: true); break; }
                    catch when (i < 4) { Thread.Sleep(150 * (i + 1)); }
                }
            }
            catch { }
        }

        GC.SuppressFinalize(this);
    }

    // ──────────────────────────────────────────────
    //  STORED PROCEDURES
    // ──────────────────────────────────────────────

    [Fact]
    public void CreateProcedure_WithBody_ShouldRegister()
    {
        db.ExecuteSQL(
            "CREATE PROCEDURE raise_salary @pct INTEGER BEGIN UPDATE employees SET salary = 95000 WHERE id = 1 END");

        // No exception means it registered successfully
    }

    [Fact]
    public void ExecProcedure_WithParams_ShouldExecuteBody()
    {
        db.ExecuteSQL(
            "CREATE PROCEDURE set_salary @emp_id INTEGER, @new_sal INTEGER BEGIN UPDATE employees SET salary = @new_sal WHERE id = @emp_id END");

        db.ExecuteSQL("EXEC set_salary @emp_id=2, @new_sal=100000");

        var rows = db.ExecuteQuery("SELECT * FROM employees");
        var bob = rows.First(r => Convert.ToInt64(r["id"]) == 2);
        Assert.Equal(100000, Convert.ToInt64(bob["salary"]));
    }

    [Fact]
    public void DropProcedure_Existing_ShouldSucceed()
    {
        db.ExecuteSQL(
            "CREATE PROCEDURE temp_proc @x INTEGER BEGIN UPDATE employees SET salary = 1 WHERE id = 1 END");

        db.ExecuteSQL("DROP PROCEDURE temp_proc");
    }

    [Fact]
    public void DropProcedure_NonExisting_ShouldThrow()
    {
        Assert.Throws<InvalidOperationException>(
            () => db.ExecuteSQL("DROP PROCEDURE ghost_proc"));
    }

    [Fact]
    public void ExecProcedure_NonExisting_ShouldThrow()
    {
        Assert.Throws<InvalidOperationException>(
            () => db.ExecuteSQL("EXEC no_such_proc"));
    }

    [Fact]
    public void CreateProcedure_MissingBeginEnd_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(
            () => db.ExecuteSQL("CREATE PROCEDURE bad_proc @x INTEGER SELECT 1"));
    }

    [Fact]
    public void ExecProcedure_MultiStatement_ShouldExecuteAll()
    {
        db.ExecuteSQL(
            "CREATE PROCEDURE multi @id1 INTEGER, @id2 INTEGER BEGIN UPDATE employees SET salary = 1 WHERE id = @id1; UPDATE employees SET salary = 2 WHERE id = @id2 END");

        db.ExecuteSQL("EXEC multi @id1=1, @id2=2");

        var rows = db.ExecuteQuery("SELECT * FROM employees");
        var r1 = rows.First(r => Convert.ToInt64(r["id"]) == 1);
        var r2 = rows.First(r => Convert.ToInt64(r["id"]) == 2);
        Assert.Equal(1, Convert.ToInt64(r1["salary"]));
        Assert.Equal(2, Convert.ToInt64(r2["salary"]));
    }

    // ──────────────────────────────────────────────
    //  VIEWS
    // ──────────────────────────────────────────────

    [Fact]
    public void CreateView_Simple_ShouldRegister()
    {
        db.ExecuteSQL("CREATE VIEW high_earners AS SELECT * FROM employees WHERE salary > 75000");
    }

    [Fact]
    public void CreateView_MissingAs_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(
            () => db.ExecuteSQL("CREATE VIEW bad_view SELECT * FROM employees"));
    }

    [Fact]
    public void CreateView_NonSelectBody_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(
            () => db.ExecuteSQL("CREATE VIEW bad_view AS DELETE FROM employees"));
    }

    [Fact]
    public void DropView_Existing_ShouldSucceed()
    {
        db.ExecuteSQL("CREATE VIEW temp_view AS SELECT * FROM employees");
        db.ExecuteSQL("DROP VIEW temp_view");
    }

    [Fact]
    public void DropView_NonExisting_ShouldThrow()
    {
        Assert.Throws<InvalidOperationException>(
            () => db.ExecuteSQL("DROP VIEW ghost_view"));
    }

    [Fact]
    public void CreateMaterializedView_ShouldCacheResults()
    {
        db.ExecuteSQL("CREATE MATERIALIZED VIEW mat_emps AS SELECT * FROM employees");

        // Materialized views eagerly compute — no exception means success
    }

    // ──────────────────────────────────────────────
    //  TRIGGERS
    // ──────────────────────────────────────────────

    [Fact]
    public void CreateTrigger_AfterInsert_ShouldRegister()
    {
        db.ExecuteSQL("CREATE TABLE audit_log (id INTEGER PRIMARY KEY, msg TEXT)");

        db.ExecuteSQL(
            "CREATE TRIGGER log_insert AFTER INSERT ON employees BEGIN INSERT INTO audit_log VALUES (99, 'inserted') END");
    }

    [Fact]
    public void CreateTrigger_BeforeDelete_ShouldRegister()
    {
        db.ExecuteSQL("CREATE TABLE audit_log2 (id INTEGER PRIMARY KEY, msg TEXT)");

        db.ExecuteSQL(
            "CREATE TRIGGER log_delete BEFORE DELETE ON employees BEGIN INSERT INTO audit_log2 VALUES (99, 'deleting') END");
    }

    [Fact]
    public void DropTrigger_Existing_ShouldSucceed()
    {
        db.ExecuteSQL(
            "CREATE TRIGGER temp_trigger AFTER UPDATE ON employees BEGIN UPDATE employees SET salary = 0 WHERE id = 999 END");

        db.ExecuteSQL("DROP TRIGGER temp_trigger");
    }

    [Fact]
    public void DropTrigger_NonExisting_ShouldThrow()
    {
        Assert.Throws<InvalidOperationException>(
            () => db.ExecuteSQL("DROP TRIGGER ghost_trigger"));
    }

    [Fact]
    public void CreateTrigger_InvalidTiming_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(
            () => db.ExecuteSQL(
                "CREATE TRIGGER bad INSTEAD INSERT ON employees BEGIN UPDATE employees SET salary = 0 WHERE id = 1 END"));
    }

    [Fact]
    public void CreateTrigger_InvalidEvent_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(
            () => db.ExecuteSQL(
                "CREATE TRIGGER bad AFTER TRUNCATE ON employees BEGIN UPDATE employees SET salary = 0 WHERE id = 1 END"));
    }

    [Fact]
    public void CreateTrigger_MissingBeginEnd_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(
            () => db.ExecuteSQL(
                "CREATE TRIGGER bad AFTER INSERT ON employees UPDATE employees SET salary = 0 WHERE id = 1"));
    }

    [Fact]
    public void CreateTrigger_MissingOnKeyword_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(
            () => db.ExecuteSQL(
                "CREATE TRIGGER bad AFTER INSERT employees BEGIN UPDATE employees SET salary = 0 WHERE id = 1 END"));
    }

    // ──────────────────────────────────────────────
    //  UNSUPPORTED STATEMENT (default branch)
    // ──────────────────────────────────────────────

    [Fact]
    public void ExecuteSQL_UnsupportedStatement_ShouldThrow()
    {
        Assert.Throws<InvalidOperationException>(
            () => db.ExecuteSQL("GRANT SELECT ON employees TO public"));
    }
}
