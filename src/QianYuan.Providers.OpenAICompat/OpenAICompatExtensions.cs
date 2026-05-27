using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;

namespace QianYuan.Providers.OpenAICompat;

public static class OpenAICompatExtensions
{
    /// <summary>
    /// Register an OpenAI-compatible provider. Call multiple times to add several
    /// (e.g. GPT + Kimi + MiniMax + Qwen-compat). Each must have a unique ProviderId.
    /// Auto-registers into <see cref="ILlmProviderRegistry"/> at first resolution.
    /// </summary>
    public static IServiceCollection AddOpenAICompatProvider(this IServiceCollection services, OpenAICompatOptions options)
    {
        services.AddHttpClient($"qianyuan.{options.ProviderId}");

        services.AddSingleton<ILlmProvider>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient($"qianyuan.{options.ProviderId}");
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<OpenAICompatProvider>();
            var provider = new OpenAICompatProvider(http, options, logger);

            var registry = sp.GetService<ILlmProviderRegistry>();
            registry?.Register(provider);
            return provider;
        });
        return services;
    }
}
