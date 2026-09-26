using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lateral.CMS.UnitTests.Support;

/// <summary>
/// A private SQLite database per test. In-memory, so a test is isolated and needs no infrastructure, but
/// still a relational store: unique indexes and concurrency tokens behave as they do in production.
/// </summary>
public sealed class CmsTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public CmsTestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TestCmsDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new TestCmsDbContext(options);
        Context.GetService<IRelationalDatabaseCreator>().CreateTables();
    }

    public TestCmsDbContext Context { get; }

    /// <summary>A context of its own over the same database, to read back what a handler wrote.</summary>
    public TestCmsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestCmsDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new TestCmsDbContext(options);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
