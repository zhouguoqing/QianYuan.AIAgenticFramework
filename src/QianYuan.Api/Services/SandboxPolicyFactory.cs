using QianYuan.Api.Controllers;
using QianYuan.Core.Sandbox;

namespace QianYuan.Api.Services;

public interface IChatSandboxPolicyService
{
    SandboxPolicySnapshot Resolve(ChatStreamRequest request, string sessionId, string? providerId = null, string? model = null);
}

public sealed class ChatSandboxPolicyService : IChatSandboxPolicyService
{
    public SandboxPolicySnapshot Resolve(ChatStreamRequest request, string sessionId, string? providerId = null, string? model = null)
    {
        return new SandboxPolicySnapshot
        {
            SessionId = sessionId,
            OwnerId = TrimToNull(request.OwnerId) ?? "anonymous",
            WorkspaceId = TrimToNull(request.WorkspaceId) ?? "default-workspace",
            WorkspaceRoot = ResolveWorkspaceRoot(request.WorkspacePath),
            WorkspaceLabel = TrimToNull(request.WorkspaceLabel) ?? "default-workspace",
            Permission = TrimToNull(request.Permission),
            ProviderId = TrimToNull(providerId ?? request.Provider),
            Model = TrimToNull(model ?? request.Model),
        };
    }

    private static string? ResolveWorkspaceRoot(string? workspacePath)
    {
        var trimmed = TrimToNull(workspacePath);
        if (trimmed is null) return null;

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return null;
        }
    }

    private static string? TrimToNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}