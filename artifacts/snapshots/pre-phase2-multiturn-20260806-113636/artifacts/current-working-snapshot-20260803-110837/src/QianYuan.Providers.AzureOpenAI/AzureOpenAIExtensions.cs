using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;

namespace QianYuan.Providers.AzureOpenAI;

public static class AzureOpenAIExtensions
{
    /// <summary>
    /// Register an Azure OpenAI provider. Each call must have a unique <see cref="AzureOpenAIOptions.ProviderId"/>.
    /// Auto-registers into <see cref="ILlmProviderRegistry"/> on first resolution.
    /// </summary>
    public static IServiceCollection AddAzureOpenAIProvider(this IServiceCollection services, AzureOpenAIOptions options)
    {
        services.AddHttpClient($"qianyuan.{options.ProviderId}");

        services.AddSingleton<ILlmProvider>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient($"qianyuan.{options.ProviderId}");
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<AzureOpenAIProvider>();
            var provider = new AzureOpenAIProvider(http, options, logger);

            var registry = sp.GetService<ILlmProviderRegistry>();
            registry?.Register(provider);
            return provider;
        });
        return services;
    }
}
