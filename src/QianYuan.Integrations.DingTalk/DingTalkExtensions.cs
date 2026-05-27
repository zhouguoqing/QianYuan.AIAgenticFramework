using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace QianYuan.Integrations.DingTalk;

public static class DingTalkExtensions
{
    public static IServiceCollection AddDingTalkIntegration(this IServiceCollection services, Action<DingTalkOptions> configure)
    {
        services.Configure(configure);
        services.AddHttpClient<DingTalkOutgoingClient>();
        services.AddSingleton<DingTalkInboundHandler>();
        return services;
    }
}
