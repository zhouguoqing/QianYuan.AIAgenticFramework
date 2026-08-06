using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;

namespace QianYuan.Providers.Anthropic;

public static class AnthropicExtensions
{
    public static IServiceCollection AddAnthropicProvider(this IServiceCollection services, AnthropicOptions options)
    {
        services.AddHttpClient($"qianyuan.{options.ProviderId}");
        services.AddSingleton<ILlmProvider>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient($"qianyuan.{options.ProviderId}");
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<AnthropicProvider>();
            var p = new AnthropicProvider(http, options, logger);
            sp.GetService<ILlmProviderRegistry>()?.Register(p);
            return p;
        });
        return services;
    }
}
