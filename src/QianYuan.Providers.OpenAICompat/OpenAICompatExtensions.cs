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
        // Bypass the system/forwarding proxy (e.g. http_proxy env var). LLM API traffic
        // must go direct to the endpoint; a forwarding proxy can corrupt the chunked
        // transfer-encoding of the request body, causing upstream gateways (new-api,
        // one-api, etc.) to reject with "invalid byte in chunk length" (HTTP 400).
        services.AddHttpClient($"qianyuan.{options.ProviderId}")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseProxy = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            });

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
