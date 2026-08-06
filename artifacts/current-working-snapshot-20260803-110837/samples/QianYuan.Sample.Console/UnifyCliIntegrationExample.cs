using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QianYuan.UnifyCli;
using QianYuan.UnifyCli.Abstractions;
using QianYuan.UnifyCli.Examples;
using QianYuan.UnifyCli.Implementation;
using QianYuan.UnifyCli.Skills;

namespace QianYuan.Sample.Console;

/// <summary>
/// Example: 如何在应用中集成 UnifyCli 和 Skill
/// Demonstrates how to integrate UnifyCli services into the application and expose them as Skills.
/// </summary>
public sealed class UnifyCliIntegrationExample
{
    /// <summary>
    /// 基本用法示例 - Basic Usage Example
    /// </summary>
    public static async Task BasicUsageExample()
    {
        System.Console.WriteLine("=== UnifyCli 基本用法示例 ===\n");

        // 创建天气服务
        var weatherService = WeatherServiceExample.CreateWeatherService();

        // 调用获取当前天气的方法
        var result = await weatherService.InvokeAsync(
            "get_current_weather",
            @"{""city"": ""Beijing"", ""units"": ""metric""}");

        System.Console.WriteLine($"成功: {!result.IsError}");
        System.Console.WriteLine($"状态码: {result.StatusCode}");
        System.Console.WriteLine($"执行时间: {result.ExecutionTimeMs}ms");
        System.Console.WriteLine($"结果: {result.JsonContent}");
        System.Console.WriteLine($"摘要: {result.HumanSummary}\n");
    }

    /// <summary>
    /// 认证示例 - Authentication Example
    /// </summary>
    public static async Task AuthenticationExample()
    {
        System.Console.WriteLine("=== 认证示例 ===\n");

        // 使用 GitHub Token 创建服务
        var githubService = GitHubServiceExample.CreateGitHubService("your-github-token");

        // 获取仓库信息
        var result = await githubService.InvokeAsync(
            "get_repository",
            @"{""owner"": ""dotnet"", ""repo"": ""runtime""}");

        System.Console.WriteLine($"成功: {!result.IsError}");
        System.Console.WriteLine($"结果摘要: {result.HumanSummary}\n");
    }

    /// <summary>
    /// 注册表和发现示例 - Registry and Discovery Example
    /// </summary>
    public static async Task RegistryAndDiscoveryExample()
    {
        System.Console.WriteLine("=== 注册表和发现示例 ===\n");

        // 创建注册表
        var registry = new CliServiceRegistry();

        // 注册多个服务
        var weatherService = WeatherServiceExample.CreateWeatherService();
        var githubService = GitHubServiceExample.CreateGitHubService();
        var slackService = SlackServiceExample.CreateSlackService();

        registry.Register(weatherService);
        registry.Register(githubService);
        registry.Register(slackService);

        // 列出所有已注册的服务
        var manifests = registry.ListManifests();
        System.Console.WriteLine($"已注册服务数量: {manifests.Count}");
        foreach (var manifest in manifests)
        {
            System.Console.WriteLine($"  - {manifest.Name} ({manifest.Id})");
            System.Console.WriteLine($"    标签: {string.Join(", ", manifest.Tags)}");
        }

        // 搜索相关服务
        var searchResults = await registry.SearchAsync(new[] { "weather", "forecast" });
        System.Console.WriteLine($"\n搜索 'weather' 结果: {searchResults.Count} 个服务");

        System.Console.WriteLine();
    }

    /// <summary>
    /// DI 集成示例 - Dependency Injection Integration Example
    /// </summary>
    public static async Task DependencyInjectionExample()
    {
        System.Console.WriteLine("=== DI 集成示例 ===\n");

        var services = new ServiceCollection();
        
        // 添加日志记录
        services.AddLogging(builder => builder.AddConsole());

        // 添加 UnifyCli
        services.AddUnifyCli();

        // 注册服务
        var weatherService = WeatherServiceExample.CreateWeatherService();
        services.AddCliService(weatherService);

        var sp = services.BuildServiceProvider();

        // 从 DI 获取注册表
        var registry = sp.GetRequiredService<ICliServiceRegistry>();

        // 列出所有服务
        System.Console.WriteLine("已注册的 CLI 服务:");
        foreach (var manifest in registry.ListManifests())
        {
            System.Console.WriteLine($"  - {manifest.Name} (ID: {manifest.Id})");
        }

        System.Console.WriteLine();
    }

    /// <summary>
    /// Skill 集成示例 - Skill Integration Example
    /// </summary>
    public static async Task SkillIntegrationExample()
    {
        System.Console.WriteLine("=== Skill 集成示例 ===\n");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());

        // 添加 UnifyCli 和 Skill
        services.AddUnifyCli();
        
        // 注册 CLI 服务
        var weatherService = WeatherServiceExample.CreateWeatherService();
        services.AddCliService(weatherService);

        var sp = services.BuildServiceProvider();

        // 创建 Skill
        var skillFactory = sp.GetRequiredService<CliServiceSkillFactory>();
        var skill = await skillFactory.CreateSkillAsync("weather.openweathermap");

        if (skill != null)
        {
            System.Console.WriteLine($"已创建 Skill: {skill.Name}");
            System.Console.WriteLine($"描述: {skill.Description}");
            
            // 获取该 Skill 公开的工具
            var tools = await skill.GetToolsAsync();
            System.Console.WriteLine($"\n公开的工具数量: {tools.Count}");
            foreach (var tool in tools)
            {
                System.Console.WriteLine($"  - {tool.Name}: {tool.Description}");
            }
        }

        System.Console.WriteLine();
    }

    /// <summary>
    /// 错误处理示例 - Error Handling Example
    /// </summary>
    public static async Task ErrorHandlingExample()
    {
        System.Console.WriteLine("=== 错误处理示例 ===\n");

        var weatherService = WeatherServiceExample.CreateWeatherService();

        // 尝试调用不存在的方法
        var result = await weatherService.InvokeAsync(
            "nonexistent_method",
            @"{""city"": ""Beijing""}");

        System.Console.WriteLine($"成功: {!result.IsError}");
        System.Console.WriteLine($"错误信息: {result.HumanSummary}");
        System.Console.WriteLine($"JSON: {result.JsonContent}\n");
    }

    /// <summary>
    /// 完整的应用配置示例 - Full Application Configuration Example
    /// </summary>
    public static void FullApplicationConfigurationExample()
    {
        System.Console.WriteLine("=== 完整应用配置示例 ===\n");

        var services = new ServiceCollection();

        // 1. 添加基础设置
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 2. 添加 UnifyCli
        services.AddUnifyCli();

        // 3. 注册多个 CLI 服务
        services.AddCliService(WeatherServiceExample.CreateWeatherService());
        services.AddCliService(GitHubServiceExample.CreateGitHubService("your-github-token"));
        services.AddCliService(SlackServiceExample.CreateSlackService("your-slack-token"));

        // 4. 自动注册 CLI Skill（可选）
        services.AddCliServiceSkill("weather.openweathermap");
        services.AddCliServiceSkill("vcs.github");
        services.AddCliServiceSkill("messaging.slack");

        var sp = services.BuildServiceProvider();

        // 5. 使用注册表
        var registry = sp.GetRequiredService<ICliServiceRegistry>();
        var manifests = registry.ListManifests();

        System.Console.WriteLine($"已注册 {manifests.Count} 个 CLI 服务:");
        foreach (var manifest in manifests)
        {
            System.Console.WriteLine($"  ✓ {manifest.Name}");
            System.Console.WriteLine($"    - ID: {manifest.Id}");
            System.Console.WriteLine($"    - 标签: {string.Join(", ", manifest.Tags)}");
            System.Console.WriteLine($"    - 需要认证: {manifest.RequiresAuthentication}");
        }

        System.Console.WriteLine();
    }

    public static void Main(string[] args)
    {
        System.Console.WriteLine("\n╔════════════════════════════════════════╗");
        System.Console.WriteLine("║   QianYuan UnifyCli 集成示例        ║");
        System.Console.WriteLine("╚════════════════════════════════════════╝\n");

        // 运行示例
        Task.Run(async () =>
        {
            try
            {
                // 取消注释要运行的示例
                await BasicUsageExample();
                // await AuthenticationExample();
                await RegistryAndDiscoveryExample();
                await DependencyInjectionExample();
                await SkillIntegrationExample();
                await ErrorHandlingExample();
                FullApplicationConfigurationExample();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"错误: {ex.Message}");
            }
        }).Wait();

        System.Console.WriteLine("示例完成!");
    }
}
