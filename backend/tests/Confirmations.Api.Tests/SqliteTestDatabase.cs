using Confirmations.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Confirmations.Api.Tests;

/// <summary>
/// A real (but tiny, in-memory) relational database for tests, so unique
/// indexes, foreign keys, and concurrency checks are actually enforced.
/// The app itself uses SQL Server Express.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public string UserId { get; } = Guid.NewGuid().ToString();

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var db = NewContext();
        db.Database.EnsureCreated();
        db.Users.Add(new IdentityUser
        {
            Id = UserId,
            UserName = "employee@demo.local",
            Email = "employee@demo.local",
        });
        db.SaveChanges();
    }

    /// <summary>A fresh context, like a new web request would get.</summary>
    public AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    /// <summary>Reads a single value with plain SQL (to see how data is really stored).</summary>
    public object? ScalarSql(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public void Dispose() => _connection.Dispose();
}
