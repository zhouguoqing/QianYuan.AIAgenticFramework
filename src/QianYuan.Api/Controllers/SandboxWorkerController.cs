using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using QianYuan.Api.Configuration;
using QianYuan.Api.Services;
using QianYuan.Core.Sandbox;

namespace QianYuan.Api.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/internal/sandbox")]
public sealed class SandboxWorkerController : ControllerBase
{
    private readonly LocalCodeExecutionWorkerClient _localWorker;
    private readonly QianYuanApiOptions _options;

    public SandboxWorkerController(LocalCodeExecutionWorkerClient localWorker, IOptions<QianYuanApiOptions> options)
    {
        _localWorker = localWorker;
        _options = options.Value;
    }

    [HttpPost("code-exec")]
    public async Task<ActionResult<CodeExecutionWorkerResponse>> ExecuteCode(
        [FromBody] CodeExecutionWorkerRequest request,
        CancellationToken ct)
    {
        if (!IsWorkerRequestAuthorized())
            return Unauthorized(new CodeExecutionWorkerResponse
            {
                Succeeded = false,
                ExitCode = -1,
                WorkerId = "internal-gateway",
                ErrorCode = "UNAUTHORIZED",
                ErrorMessage = "sandbox worker token validation failed",
            });

        var result = await _localWorker.ExecuteAsync(request, ct).ConfigureAwait(false);
        return Ok(result);
    }

    private bool IsWorkerRequestAuthorized()
    {
        var configured = _options.SandboxWorker.AuthToken;
        if (string.IsNullOrWhiteSpace(configured)) return true;

        if (!Request.Headers.TryGetValue("X-Sandbox-Worker-Token", out var incoming))
            return false;

        return string.Equals(incoming.ToString(), configured, StringComparison.Ordinal);
    }
}
