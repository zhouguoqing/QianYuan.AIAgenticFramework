using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;

namespace QianYuan.Providers.QwenNative;

public static class QwenExtensions
{
    public static IServiceCollection AddQwenProvider(this IServiceCollection services, QwenOptions options)
    {
        services.AddHttpClient($"qianyuan.{options.ProviderId}");
        services.AddSingleton<ILlmProvider>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient($"qianyuan.{options.ProviderId}");
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<QwenProvider>();
            var p = new QwenProvider(http, options, logger);
            sp.GetService<ILlmProviderRegistry>()?.Register(p);
            return p;
        });
        return services;
    }
}
