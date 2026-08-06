using Microsoft.Extensions.DependencyInjection;
using QianYuan.Core.Abstractions;
using QianYuan.Skills.Builtin.Code;
using QianYuan.Skills.Builtin.FileSystem;
using QianYuan.Skills.Builtin.Vision;
using QianYuan.Skills.Builtin.WebSearch;

namespace QianYuan.Skills.Builtin;

public static class BuiltinSkillsExtensions
{
    public static IServiceCollection AddTavilyWebSearchSkill(this IServiceCollection services, string apiKey)
    {
        services.AddHttpClient("qianyuan.skills.tavily");
        services.AddSingleton<ISkill>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("qianyuan.skills.tavily");
            return new WebSearchSkill(new TavilySearchProvider(http, apiKey));
        });
        return services;
    }

    /// <summary>
    /// Register the DuckDuckGo HTML-endpoint search skill. Requires no API key and so makes a good
    /// zero-config default. DDG rate-limits scraping; for heavy production use prefer Tavily/Bing/Brave.
    /// </summary>
    public static IServiceCollection AddDuckDuckGoWebSearchSkill(this IServiceCollection services)
    {
        services.AddHttpClient("qianyuan.skills.duckduckgo");
        services.AddSingleton<ISkill>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("qianyuan.skills.duckduckgo");
            return new WebSearchSkill(new DuckDuckGoSearchProvider(http));
        });
        return services;
    }

    public static IServiceCollection AddBingWebSearchSkill(this IServiceCollection services, string apiKey)
    {
        services.AddHttpClient("qianyuan.skills.bing");
        services.AddSingleton<ISkill>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("qianyuan.skills.bing");
            return new WebSearchSkill(new BingSearchProvider(http, apiKey));
        });
        return services;
    }

    public static IServiceCollection AddBraveWebSearchSkill(this IServiceCollection services, string apiKey)
    {
        services.AddHttpClient("qianyuan.skills.brave");
        services.AddSingleton<ISkill>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("qianyuan.skills.brave");
            return new WebSearchSkill(new BraveSearchProvider(http, apiKey));
        });
        return services;
    }

    public static IServiceCollection AddVisionSkill(this IServiceCollection services)
    {
        services.AddSingleton<ISkill, VisionSkill>();
        return services;
    }

    public static IServiceCollection AddFileSystemSkill(this IServiceCollection services, string sandboxRoot, bool readOnly = false)
    {
        services.AddSingleton<ISkill>(_ => new FileSystemSkill(sandboxRoot, readOnly));
        return services;
    }

    public static IServiceCollection AddCodeExecutionSkill(this IServiceCollection services, CodeExecutionOptions options)
    {
        services.AddSingleton<ISkill>(_ => new CodeExecutionSkill(options));
        return services;
    }

    /// <summary>
    /// After building the DI container, register every <see cref="ISkill"/> resolved from DI
    /// into the <see cref="ISkillManager"/> so they show up in catalogs and progressive selection.
    /// </summary>
    public static void RegisterSkillsFromServices(this IServiceProvider sp)
    {
        var manager = sp.GetRequiredService<ISkillManager>();
        foreach (var skill in sp.GetServices<ISkill>())
        {
            manager.Register(skill);
        }
    }
}
