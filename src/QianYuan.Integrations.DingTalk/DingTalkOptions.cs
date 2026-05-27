namespace QianYuan.Integrations.DingTalk;

public sealed class DingTalkOptions
{
    /// <summary>Outgoing bot webhook URL (custom robot). e.g. https://oapi.dingtalk.com/robot/send?access_token=xxx</summary>
    public string? OutgoingWebhookUrl { get; set; }

    /// <summary>Signing secret used to sign &amp; timestamp outgoing webhook calls (custom-robot signing).</summary>
    public string? OutgoingSecret { get; set; }

    /// <summary>App key used to verify inbound callbacks (callback HMAC signature).</summary>
    public string? AppSecret { get; set; }

    /// <summary>Default agent id that handles inbound messages.</summary>
    public string DefaultAgentId { get; set; } = "qianyuan.default";
}
