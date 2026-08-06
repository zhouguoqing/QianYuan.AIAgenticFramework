using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;

namespace QianYuan.Providers.Gemini;

public static class GeminiExtensions
{
    public static IServiceCollection AddGeminiProvider(this IServiceCollection services, GeminiOptions options)
    {
        services.AddHttpClient($"qianyuan.{options.ProviderId}");
        services.AddSingleton<ILlmProvider>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient($"qianyuan.{options.ProviderId}");
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<GeminiProvider>();
            var p = new GeminiProvider(http, options, logger);
            sp.GetService<ILlmProviderRegistry>()?.Register(p);
            return p;
        });
        return services;
    }
}
