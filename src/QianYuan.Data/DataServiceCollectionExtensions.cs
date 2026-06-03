using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using QianYuan.Data.Repositories;
using QianYuan.Data.Services;

namespace QianYuan.Data;

/// <summary>
/// 数据层 DI 扩展
/// </summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// 添加 QianYuan 数据访问服务
    /// </summary>
    public static IServiceCollection AddQianYuanData(
        this IServiceCollection services,
        string connectionString,
        string encryptionKey)
    {
        var provider = DetectDatabaseProvider(connectionString);
        if (provider == DatabaseProvider.Sqlite)
        {
            EnsureSqliteDirectory(connectionString);
        }

        // 注册 DbContext
        services.AddDbContext<QianYuanDbContext>(options =>
        {
            // 根据连接字符串自动选择数据库提供程序
            if (provider == DatabaseProvider.SqlServer)
            {
                options.UseSqlServer(connectionString);
            }
            else
            {
                options.UseSqlite(connectionString);
            }
        });

        // 注册加密服务
        services.AddSingleton<IEncryptionService>(
            new AesEncryptionService(encryptionKey));

        // 注册仓储
        services.AddScoped<IAgentRepository, AgentRepository>();

        return services;
    }

    private enum DatabaseProvider
    {
        Sqlite,
        SqlServer
    }

    private static DatabaseProvider DetectDatabaseProvider(string connectionString)
    {
        if (connectionString.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Trusted_Connection=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Integrated Security=", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProvider.SqlServer;
        }

        return DatabaseProvider.Sqlite;
    }

    private static void EnsureSqliteDirectory(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// 初始化数据库（创建必要的表）
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<QianYuanDbContext>();
        
        // 创建数据库和表
        await context.Database.EnsureCreatedAsync();
    }
}
