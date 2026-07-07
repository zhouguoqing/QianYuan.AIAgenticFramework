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
        string encryptionKey,
        string? providerName = null)
    {
        var provider = DetectDatabaseProvider(connectionString, providerName);
        if (provider == DatabaseProvider.Sqlite)
        {
            EnsureSqliteDirectory(connectionString);
        }

        // 注册 DbContext
        services.AddDbContext<QianYuanDbContext>(options =>
        {
            if (provider == DatabaseProvider.SqlServer)
            {
                options.UseSqlServer(connectionString);
            }
            else if (provider == DatabaseProvider.PostgreSql)
            {
                options.UseNpgsql(connectionString);
            }
            else if (provider == DatabaseProvider.MySql)
            {
                throw new NotSupportedException("MySQL provider support is planned, but the EF Core 10-compatible MySQL provider is not available in the current NuGet feed. Use PostgreSQL, SQL Server, or SQLite for now.");
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
        SqlServer,
        PostgreSql,
        MySql
    }

    private static DatabaseProvider DetectDatabaseProvider(string connectionString, string? providerName)
    {
        var normalizedProvider = (providerName ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedProvider is "postgres" or "postgresql" or "pg") return DatabaseProvider.PostgreSql;
        if (normalizedProvider is "mysql" or "mariadb") return DatabaseProvider.MySql;
        if (normalizedProvider is "sqlserver" or "mssql") return DatabaseProvider.SqlServer;
        if (normalizedProvider is "sqlite" or "sqlite3") return DatabaseProvider.Sqlite;

        if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Username=", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProvider.PostgreSql;
        }

        if (connectionString.Contains("Uid=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("User Id=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Allow User Variables=", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProvider.MySql;
        }

        if (connectionString.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase) ||
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
        await EnsureAccountTablesAsync(context);
    }

    private static async Task EnsureAccountTablesAsync(QianYuanDbContext context)
    {
        var providerName = context.Database.ProviderName ?? string.Empty;
        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "UserAccounts" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_UserAccounts" PRIMARY KEY,
                    "Email" TEXT NOT NULL,
                    "DisplayName" TEXT NOT NULL,
                    "PasswordHash" TEXT NOT NULL,
                    "Status" TEXT NOT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserAccounts_Email" ON "UserAccounts" ("Email");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "RefreshTokens" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_RefreshTokens" PRIMARY KEY,
                    "UserId" TEXT NOT NULL,
                    "TokenHash" TEXT NOT NULL,
                    "ExpiresAt" TEXT NOT NULL,
                    "RevokedAt" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_RefreshTokens_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_RefreshTokens_TokenHash" ON "RefreshTokens" ("TokenHash");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_RefreshTokens_UserId" ON "RefreshTokens" ("UserId");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "CreditWallets" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_CreditWallets" PRIMARY KEY,
                    "UserId" TEXT NOT NULL,
                    "Balance" INTEGER NOT NULL,
                    "MonthlyQuota" INTEGER NOT NULL,
                    "QuotaMonth" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_CreditWallets_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_CreditWallets_UserId" ON "CreditWallets" ("UserId");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "CreditTransactions" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_CreditTransactions" PRIMARY KEY,
                    "UserId" TEXT NOT NULL,
                    "Type" TEXT NOT NULL,
                    "Amount" INTEGER NOT NULL,
                    "BalanceAfter" INTEGER NOT NULL,
                    "SourceType" TEXT NOT NULL,
                    "SourceId" TEXT NULL,
                    "Description" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_CreditTransactions_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_CreditTransactions_UserId_CreatedAt" ON "CreditTransactions" ("UserId", "CreatedAt");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "SubscriptionPlans" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_SubscriptionPlans" PRIMARY KEY,
                    "Name" TEXT NOT NULL,
                    "MonthlyCredits" INTEGER NOT NULL,
                    "MaxAssistants" INTEGER NOT NULL,
                    "MaxProjects" INTEGER NOT NULL,
                    "MaxAutoTasks" INTEGER NOT NULL,
                    "AllowAllModels" INTEGER NOT NULL,
                    "PriceMonthlyCents" INTEGER NOT NULL,
                    "SortOrder" INTEGER NOT NULL,
                    "Enabled" INTEGER NOT NULL
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "UserSubscriptions" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_UserSubscriptions" PRIMARY KEY,
                    "UserId" TEXT NOT NULL,
                    "PlanId" TEXT NOT NULL,
                    "Status" TEXT NOT NULL,
                    "StartedAt" TEXT NOT NULL,
                    "ExpiresAt" TEXT NULL,
                    CONSTRAINT "FK_UserSubscriptions_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_UserSubscriptions_SubscriptionPlans_PlanId" FOREIGN KEY ("PlanId") REFERENCES "SubscriptionPlans" ("Id") ON DELETE RESTRICT
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_UserSubscriptions_UserId_Status" ON "UserSubscriptions" ("UserId", "Status");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "WorkTasks" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_WorkTasks" PRIMARY KEY,
                    "UserId" TEXT NOT NULL,
                    "Title" TEXT NOT NULL,
                    "Goal" TEXT NOT NULL,
                    "Status" TEXT NOT NULL,
                    "TeamId" TEXT NULL,
                    "ProviderId" TEXT NULL,
                    "Model" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_WorkTasks_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_WorkTasks_UserId_UpdatedAt" ON "WorkTasks" ("UserId", "UpdatedAt");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "WorkSteps" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_WorkSteps" PRIMARY KEY,
                    "TaskId" TEXT NOT NULL,
                    "UserId" TEXT NOT NULL,
                    "StepOrder" INTEGER NOT NULL,
                    "Name" TEXT NOT NULL,
                    "Status" TEXT NOT NULL,
                    "AgentId" TEXT NULL,
                    "Summary" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_WorkSteps_WorkTasks_TaskId" FOREIGN KEY ("TaskId") REFERENCES "WorkTasks" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_WorkSteps_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_WorkSteps_TaskId_StepOrder" ON "WorkSteps" ("TaskId", "StepOrder");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "WorkArtifacts" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_WorkArtifacts" PRIMARY KEY,
                    "TaskId" TEXT NOT NULL,
                    "UserId" TEXT NOT NULL,
                    "Name" TEXT NOT NULL,
                    "ContentType" TEXT NOT NULL,
                    "StorageKind" TEXT NOT NULL,
                    "Content" TEXT NULL,
                    "FilePath" TEXT NULL,
                    "SizeBytes" INTEGER NOT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_WorkArtifacts_WorkTasks_TaskId" FOREIGN KEY ("TaskId") REFERENCES "WorkTasks" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_WorkArtifacts_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_WorkArtifacts_TaskId_CreatedAt" ON "WorkArtifacts" ("TaskId", "CreatedAt");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "ExpertTeams" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_ExpertTeams" PRIMARY KEY,
                    "UserId" TEXT NOT NULL,
                    "Name" TEXT NOT NULL,
                    "Description" TEXT NOT NULL,
                    "Scenario" TEXT NOT NULL,
                    "Enabled" INTEGER NOT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_ExpertTeams_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_ExpertTeams_UserId_Name" ON "ExpertTeams" ("UserId", "Name");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "ExpertTeamMembers" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_ExpertTeamMembers" PRIMARY KEY,
                    "TeamId" TEXT NOT NULL,
                    "UserId" TEXT NOT NULL,
                    "MemberOrder" INTEGER NOT NULL,
                    "RoleId" TEXT NOT NULL,
                    "DisplayName" TEXT NOT NULL,
                    "AgentId" TEXT NOT NULL,
                    "Responsibility" TEXT NOT NULL,
                    "ExecutionMode" TEXT NOT NULL,
                    "Enabled" INTEGER NOT NULL,
                    CONSTRAINT "FK_ExpertTeamMembers_ExpertTeams_TeamId" FOREIGN KEY ("TeamId") REFERENCES "ExpertTeams" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ExpertTeamMembers_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_ExpertTeamMembers_TeamId_MemberOrder" ON "ExpertTeamMembers" ("TeamId", "MemberOrder");
                """);
        }
        else if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "UserAccounts" (
                    "Id" uuid NOT NULL,
                    "Email" character varying(320) NOT NULL,
                    "DisplayName" character varying(120) NOT NULL,
                    "PasswordHash" text NOT NULL,
                    "Status" character varying(40) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_UserAccounts" PRIMARY KEY ("Id")
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserAccounts_Email" ON "UserAccounts" ("Email");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "RefreshTokens" (
                    "Id" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "TokenHash" text NOT NULL,
                    "ExpiresAt" timestamp with time zone NOT NULL,
                    "RevokedAt" timestamp with time zone NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_RefreshTokens" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_RefreshTokens_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_RefreshTokens_TokenHash" ON "RefreshTokens" ("TokenHash");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_RefreshTokens_UserId" ON "RefreshTokens" ("UserId");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "CreditWallets" (
                    "Id" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "Balance" bigint NOT NULL,
                    "MonthlyQuota" bigint NOT NULL,
                    "QuotaMonth" character varying(7) NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_CreditWallets" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_CreditWallets_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_CreditWallets_UserId" ON "CreditWallets" ("UserId");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "CreditTransactions" (
                    "Id" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "Type" character varying(40) NOT NULL,
                    "Amount" bigint NOT NULL,
                    "BalanceAfter" bigint NOT NULL,
                    "SourceType" character varying(80) NOT NULL,
                    "SourceId" character varying(120) NULL,
                    "Description" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_CreditTransactions" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_CreditTransactions_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_CreditTransactions_UserId_CreatedAt" ON "CreditTransactions" ("UserId", "CreatedAt");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "SubscriptionPlans" (
                    "Id" character varying(40) NOT NULL,
                    "Name" character varying(80) NOT NULL,
                    "MonthlyCredits" bigint NOT NULL,
                    "MaxAssistants" integer NOT NULL,
                    "MaxProjects" integer NOT NULL,
                    "MaxAutoTasks" integer NOT NULL,
                    "AllowAllModels" boolean NOT NULL,
                    "PriceMonthlyCents" integer NOT NULL,
                    "SortOrder" integer NOT NULL,
                    "Enabled" boolean NOT NULL,
                    CONSTRAINT "PK_SubscriptionPlans" PRIMARY KEY ("Id")
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "UserSubscriptions" (
                    "Id" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "PlanId" character varying(40) NOT NULL,
                    "Status" text NOT NULL,
                    "StartedAt" timestamp with time zone NOT NULL,
                    "ExpiresAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_UserSubscriptions" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_UserSubscriptions_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_UserSubscriptions_SubscriptionPlans_PlanId" FOREIGN KEY ("PlanId") REFERENCES "SubscriptionPlans" ("Id") ON DELETE RESTRICT
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_UserSubscriptions_UserId_Status" ON "UserSubscriptions" ("UserId", "Status");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "WorkTasks" (
                    "Id" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "Title" character varying(240) NOT NULL,
                    "Goal" text NOT NULL,
                    "Status" character varying(40) NOT NULL,
                    "TeamId" text NULL,
                    "ProviderId" text NULL,
                    "Model" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_WorkTasks" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_WorkTasks_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_WorkTasks_UserId_UpdatedAt" ON "WorkTasks" ("UserId", "UpdatedAt");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "WorkSteps" (
                    "Id" uuid NOT NULL,
                    "TaskId" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "StepOrder" integer NOT NULL,
                    "Name" character varying(160) NOT NULL,
                    "Status" character varying(40) NOT NULL,
                    "AgentId" text NULL,
                    "Summary" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_WorkSteps" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_WorkSteps_WorkTasks_TaskId" FOREIGN KEY ("TaskId") REFERENCES "WorkTasks" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_WorkSteps_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_WorkSteps_TaskId_StepOrder" ON "WorkSteps" ("TaskId", "StepOrder");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "WorkArtifacts" (
                    "Id" uuid NOT NULL,
                    "TaskId" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "Name" character varying(240) NOT NULL,
                    "ContentType" character varying(120) NOT NULL,
                    "StorageKind" character varying(40) NOT NULL,
                    "Content" text NULL,
                    "FilePath" text NULL,
                    "SizeBytes" bigint NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_WorkArtifacts" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_WorkArtifacts_WorkTasks_TaskId" FOREIGN KEY ("TaskId") REFERENCES "WorkTasks" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_WorkArtifacts_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_WorkArtifacts_TaskId_CreatedAt" ON "WorkArtifacts" ("TaskId", "CreatedAt");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "ExpertTeams" (
                    "Id" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "Name" character varying(120) NOT NULL,
                    "Description" text NOT NULL,
                    "Scenario" character varying(80) NOT NULL,
                    "Enabled" boolean NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_ExpertTeams" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_ExpertTeams_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_ExpertTeams_UserId_Name" ON "ExpertTeams" ("UserId", "Name");
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "ExpertTeamMembers" (
                    "Id" uuid NOT NULL,
                    "TeamId" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "MemberOrder" integer NOT NULL,
                    "RoleId" character varying(80) NOT NULL,
                    "DisplayName" character varying(120) NOT NULL,
                    "AgentId" character varying(256) NOT NULL,
                    "Responsibility" text NOT NULL,
                    "ExecutionMode" character varying(40) NOT NULL,
                    "Enabled" boolean NOT NULL,
                    CONSTRAINT "PK_ExpertTeamMembers" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_ExpertTeamMembers_ExpertTeams_TeamId" FOREIGN KEY ("TeamId") REFERENCES "ExpertTeams" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ExpertTeamMembers_UserAccounts_UserId" FOREIGN KEY ("UserId") REFERENCES "UserAccounts" ("Id") ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_ExpertTeamMembers_TeamId_MemberOrder" ON "ExpertTeamMembers" ("TeamId", "MemberOrder");
                """);
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await context.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[UserAccounts]', N'U') IS NULL
                CREATE TABLE [UserAccounts] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_UserAccounts] PRIMARY KEY,
                    [Email] nvarchar(320) NOT NULL,
                    [DisplayName] nvarchar(120) NOT NULL,
                    [PasswordHash] nvarchar(max) NOT NULL,
                    [Status] nvarchar(40) NOT NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [UpdatedAt] datetime2 NOT NULL
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserAccounts_Email' AND object_id = OBJECT_ID(N'[UserAccounts]'))
                CREATE UNIQUE INDEX [IX_UserAccounts_Email] ON [UserAccounts] ([Email]);
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[RefreshTokens]', N'U') IS NULL
                CREATE TABLE [RefreshTokens] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_RefreshTokens] PRIMARY KEY,
                    [UserId] uniqueidentifier NOT NULL,
                    [TokenHash] nvarchar(450) NOT NULL,
                    [ExpiresAt] datetime2 NOT NULL,
                    [RevokedAt] datetime2 NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    CONSTRAINT [FK_RefreshTokens_UserAccounts_UserId] FOREIGN KEY ([UserId]) REFERENCES [UserAccounts] ([Id]) ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RefreshTokens_TokenHash' AND object_id = OBJECT_ID(N'[RefreshTokens]'))
                CREATE UNIQUE INDEX [IX_RefreshTokens_TokenHash] ON [RefreshTokens] ([TokenHash]);
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RefreshTokens_UserId' AND object_id = OBJECT_ID(N'[RefreshTokens]'))
                CREATE INDEX [IX_RefreshTokens_UserId] ON [RefreshTokens] ([UserId]);
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[CreditWallets]', N'U') IS NULL
                CREATE TABLE [CreditWallets] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_CreditWallets] PRIMARY KEY,
                    [UserId] uniqueidentifier NOT NULL,
                    [Balance] bigint NOT NULL,
                    [MonthlyQuota] bigint NOT NULL,
                    [QuotaMonth] nvarchar(7) NOT NULL,
                    [UpdatedAt] datetime2 NOT NULL,
                    CONSTRAINT [FK_CreditWallets_UserAccounts_UserId] FOREIGN KEY ([UserId]) REFERENCES [UserAccounts] ([Id]) ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CreditWallets_UserId' AND object_id = OBJECT_ID(N'[CreditWallets]'))
                CREATE UNIQUE INDEX [IX_CreditWallets_UserId] ON [CreditWallets] ([UserId]);
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[CreditTransactions]', N'U') IS NULL
                CREATE TABLE [CreditTransactions] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_CreditTransactions] PRIMARY KEY,
                    [UserId] uniqueidentifier NOT NULL,
                    [Type] nvarchar(40) NOT NULL,
                    [Amount] bigint NOT NULL,
                    [BalanceAfter] bigint NOT NULL,
                    [SourceType] nvarchar(80) NOT NULL,
                    [SourceId] nvarchar(120) NULL,
                    [Description] nvarchar(max) NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    CONSTRAINT [FK_CreditTransactions_UserAccounts_UserId] FOREIGN KEY ([UserId]) REFERENCES [UserAccounts] ([Id]) ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CreditTransactions_UserId_CreatedAt' AND object_id = OBJECT_ID(N'[CreditTransactions]'))
                CREATE INDEX [IX_CreditTransactions_UserId_CreatedAt] ON [CreditTransactions] ([UserId], [CreatedAt]);
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[SubscriptionPlans]', N'U') IS NULL
                CREATE TABLE [SubscriptionPlans] (
                    [Id] nvarchar(40) NOT NULL CONSTRAINT [PK_SubscriptionPlans] PRIMARY KEY,
                    [Name] nvarchar(80) NOT NULL,
                    [MonthlyCredits] bigint NOT NULL,
                    [MaxAssistants] int NOT NULL,
                    [MaxProjects] int NOT NULL,
                    [MaxAutoTasks] int NOT NULL,
                    [AllowAllModels] bit NOT NULL,
                    [PriceMonthlyCents] int NOT NULL,
                    [SortOrder] int NOT NULL,
                    [Enabled] bit NOT NULL
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[UserSubscriptions]', N'U') IS NULL
                CREATE TABLE [UserSubscriptions] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_UserSubscriptions] PRIMARY KEY,
                    [UserId] uniqueidentifier NOT NULL,
                    [PlanId] nvarchar(40) NOT NULL,
                    [Status] nvarchar(40) NOT NULL,
                    [StartedAt] datetime2 NOT NULL,
                    [ExpiresAt] datetime2 NULL,
                    CONSTRAINT [FK_UserSubscriptions_UserAccounts_UserId] FOREIGN KEY ([UserId]) REFERENCES [UserAccounts] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_UserSubscriptions_SubscriptionPlans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [SubscriptionPlans] ([Id]) ON DELETE NO ACTION
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserSubscriptions_UserId_Status' AND object_id = OBJECT_ID(N'[UserSubscriptions]'))
                CREATE INDEX [IX_UserSubscriptions_UserId_Status] ON [UserSubscriptions] ([UserId], [Status]);
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[WorkTasks]', N'U') IS NULL
                CREATE TABLE [WorkTasks] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_WorkTasks] PRIMARY KEY,
                    [UserId] uniqueidentifier NOT NULL,
                    [Title] nvarchar(240) NOT NULL,
                    [Goal] nvarchar(max) NOT NULL,
                    [Status] nvarchar(40) NOT NULL,
                    [TeamId] nvarchar(max) NULL,
                    [ProviderId] nvarchar(max) NULL,
                    [Model] nvarchar(max) NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [UpdatedAt] datetime2 NOT NULL,
                    CONSTRAINT [FK_WorkTasks_UserAccounts_UserId] FOREIGN KEY ([UserId]) REFERENCES [UserAccounts] ([Id]) ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkTasks_UserId_UpdatedAt' AND object_id = OBJECT_ID(N'[WorkTasks]'))
                CREATE INDEX [IX_WorkTasks_UserId_UpdatedAt] ON [WorkTasks] ([UserId], [UpdatedAt]);
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[WorkSteps]', N'U') IS NULL
                CREATE TABLE [WorkSteps] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_WorkSteps] PRIMARY KEY,
                    [TaskId] uniqueidentifier NOT NULL,
                    [UserId] uniqueidentifier NOT NULL,
                    [StepOrder] int NOT NULL,
                    [Name] nvarchar(160) NOT NULL,
                    [Status] nvarchar(40) NOT NULL,
                    [AgentId] nvarchar(max) NULL,
                    [Summary] nvarchar(max) NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [UpdatedAt] datetime2 NOT NULL,
                    CONSTRAINT [FK_WorkSteps_WorkTasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [WorkTasks] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_WorkSteps_UserAccounts_UserId] FOREIGN KEY ([UserId]) REFERENCES [UserAccounts] ([Id]) ON DELETE NO ACTION
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkSteps_TaskId_StepOrder' AND object_id = OBJECT_ID(N'[WorkSteps]'))
                CREATE INDEX [IX_WorkSteps_TaskId_StepOrder] ON [WorkSteps] ([TaskId], [StepOrder]);
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[WorkArtifacts]', N'U') IS NULL
                CREATE TABLE [WorkArtifacts] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_WorkArtifacts] PRIMARY KEY,
                    [TaskId] uniqueidentifier NOT NULL,
                    [UserId] uniqueidentifier NOT NULL,
                    [Name] nvarchar(240) NOT NULL,
                    [ContentType] nvarchar(120) NOT NULL,
                    [StorageKind] nvarchar(40) NOT NULL,
                    [Content] nvarchar(max) NULL,
                    [FilePath] nvarchar(max) NULL,
                    [SizeBytes] bigint NOT NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    CONSTRAINT [FK_WorkArtifacts_WorkTasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [WorkTasks] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_WorkArtifacts_UserAccounts_UserId] FOREIGN KEY ([UserId]) REFERENCES [UserAccounts] ([Id]) ON DELETE NO ACTION
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkArtifacts_TaskId_CreatedAt' AND object_id = OBJECT_ID(N'[WorkArtifacts]'))
                CREATE INDEX [IX_WorkArtifacts_TaskId_CreatedAt] ON [WorkArtifacts] ([TaskId], [CreatedAt]);
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[ExpertTeams]', N'U') IS NULL
                CREATE TABLE [ExpertTeams] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_ExpertTeams] PRIMARY KEY,
                    [UserId] uniqueidentifier NOT NULL,
                    [Name] nvarchar(120) NOT NULL,
                    [Description] nvarchar(max) NOT NULL,
                    [Scenario] nvarchar(80) NOT NULL,
                    [Enabled] bit NOT NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [UpdatedAt] datetime2 NOT NULL,
                    CONSTRAINT [FK_ExpertTeams_UserAccounts_UserId] FOREIGN KEY ([UserId]) REFERENCES [UserAccounts] ([Id]) ON DELETE CASCADE
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ExpertTeams_UserId_Name' AND object_id = OBJECT_ID(N'[ExpertTeams]'))
                CREATE INDEX [IX_ExpertTeams_UserId_Name] ON [ExpertTeams] ([UserId], [Name]);
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[ExpertTeamMembers]', N'U') IS NULL
                CREATE TABLE [ExpertTeamMembers] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_ExpertTeamMembers] PRIMARY KEY,
                    [TeamId] uniqueidentifier NOT NULL,
                    [UserId] uniqueidentifier NOT NULL,
                    [MemberOrder] int NOT NULL,
                    [RoleId] nvarchar(80) NOT NULL,
                    [DisplayName] nvarchar(120) NOT NULL,
                    [AgentId] nvarchar(256) NOT NULL,
                    [Responsibility] nvarchar(max) NOT NULL,
                    [ExecutionMode] nvarchar(40) NOT NULL,
                    [Enabled] bit NOT NULL,
                    CONSTRAINT [FK_ExpertTeamMembers_ExpertTeams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [ExpertTeams] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_ExpertTeamMembers_UserAccounts_UserId] FOREIGN KEY ([UserId]) REFERENCES [UserAccounts] ([Id]) ON DELETE NO ACTION
                );
                """);
            await context.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ExpertTeamMembers_TeamId_MemberOrder' AND object_id = OBJECT_ID(N'[ExpertTeamMembers]'))
                CREATE INDEX [IX_ExpertTeamMembers_TeamId_MemberOrder] ON [ExpertTeamMembers] ([TeamId], [MemberOrder]);
                """);
        }
    }
}
