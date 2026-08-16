using System.Diagnostics;
using System.Net.Http.Json;
using QianYuan.Core.Sandbox;

namespace QianYuan.Api.Services;

/// <summary>
/// Local worker endpoint that executes code in-process using subprocesses.
/// </summary>
public sealed class LocalCodeExecutionWorkerClient : ICodeExecutionWorkerClient
{
    private readonly string _workerId;

    public LocalCodeExecutionWorkerClient(string workerId)
    {
        _workerId = string.IsNullOrWhiteSpace(workerId) ? "local-sandbox-worker" : workerId.Trim();
    }

    public async ValueTask<CodeExecutionWorkerResponse> ExecuteAsync(CodeExecutionWorkerRequest request, CancellationToken ct = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var (fileName, suffix) = request.Runtime switch
            {
                "python" => ("python3", ".py"),
                "node" => ("node", ".js"),
                "bash" => ("bash", ".sh"),
                _ => (string.Empty, string.Empty)
            };

            if (string.IsNullOrEmpty(fileName))
            {
                return Failed("UNSUPPORTED_RUNTIME", $"unsupported runtime '{request.Runtime}'", request, startedAt);
            }

            Directory.CreateDirectory(request.WorkingDirectory);
            var snippetPath = Path.Combine(request.WorkingDirectory, $"snippet_{Guid.NewGuid():N}{suffix}");
            await File.WriteAllTextAsync(snippetPath, request.Code, ct).ConfigureAwait(false);

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(request.Timeout);

                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    WorkingDirectory = request.WorkingDirectory,
                };
                psi.ArgumentList.Add(snippetPath);

                using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {fileName}");
                var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
                var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);

                return new CodeExecutionWorkerResponse
                {
                    Succeeded = true,
                    TimedOut = false,
                    ExitCode = proc.ExitCode,
                    Stdout = Truncate(stdout, request.MaxOutputChars),
                    Stderr = Truncate(stderr, request.MaxOutputChars),
                    WorkerId = _workerId,
                    Attempt = request.Attempt,
                    DurationMs = ElapsedMs(startedAt),
                };
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new CodeExecutionWorkerResponse
                {
                    Succeeded = false,
                    TimedOut = true,
                    ExitCode = -1,
                    WorkerId = _workerId,
                    Attempt = request.Attempt,
                    DurationMs = ElapsedMs(startedAt),
                    ErrorCode = "TIMEOUT",
                    ErrorMessage = $"execution timed out after {request.Timeout}",
                };
            }
            finally
            {
                try { File.Delete(snippetPath); } catch { }
            }
        }
        catch (Exception ex)
        {
            return Failed("WORKER_EXCEPTION", ex.Message, request, startedAt);
        }
    }

    private CodeExecutionWorkerResponse Failed(string code, string message, CodeExecutionWorkerRequest request, long startedAt)
        => new()
        {
            Succeeded = false,
            TimedOut = false,
            ExitCode = -1,
            WorkerId = _workerId,
            Attempt = request.Attempt,
            DurationMs = ElapsedMs(startedAt),
            ErrorCode = code,
            ErrorMessage = message,
        };

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] + "...[truncated]" : s;

    private static long ElapsedMs(long startedAt)
        => (long)(1000.0 * (Stopwatch.GetTimestamp() - startedAt) / Stopwatch.Frequency);
}

/// <summary>
/// HTTP transport client for remote sandbox workers.
/// </summary>
public sealed class HttpCodeExecutionWorkerClient : ICodeExecutionWorkerClient
{
    private readonly HttpClient _httpClient;
    private readonly string _executePath;
    private readonly string? _authToken;
    private readonly int _weight;

    public string TargetId { get; }
    public int Weight => _weight;

    public HttpCodeExecutionWorkerClient(HttpClient httpClient, string targetId, int weight, string executePath, string? authToken)
    {
        _httpClient = httpClient;
        TargetId = string.IsNullOrWhiteSpace(targetId) ? "remote" : targetId.Trim();
        _weight = Math.Max(1, weight);
        _executePath = string.IsNullOrWhiteSpace(executePath) ? "/api/internal/sandbox/code-exec" : executePath;
        _authToken = string.IsNullOrWhiteSpace(authToken) ? null : authToken.Trim();
    }

    public async ValueTask<CodeExecutionWorkerResponse> ExecuteAsync(CodeExecutionWorkerRequest request, CancellationToken ct = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _executePath)
        {
            Content = JsonContent.Create(request),
        };

        if (!string.IsNullOrWhiteSpace(_authToken))
            httpRequest.Headers.TryAddWithoutValidation("X-Sandbox-Worker-Token", _authToken);

        using var response = await _httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new CodeExecutionWorkerResponse
            {
                Succeeded = false,
                ExitCode = -1,
                WorkerId = TargetId,
                ErrorCode = "HTTP_STATUS",
                ErrorMessage = $"remote worker returned {(int)response.StatusCode} {response.ReasonPhrase}",
                DurationMs = 0,
                Attempt = request.Attempt,
            };
        }

        var payload = await response.Content.ReadFromJsonAsync<CodeExecutionWorkerResponse>(cancellationToken: ct).ConfigureAwait(false);
        return payload ?? new CodeExecutionWorkerResponse
        {
            Succeeded = false,
            ExitCode = -1,
            WorkerId = TargetId,
            ErrorCode = "EMPTY_RESPONSE",
            ErrorMessage = "remote worker response payload was empty",
            DurationMs = 0,
            Attempt = request.Attempt,
        };
    }

    public async ValueTask<bool> CheckHealthAsync(string healthPath, CancellationToken ct = default)
    {
        var path = string.IsNullOrWhiteSpace(healthPath) ? "/health" : healthPath;
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrWhiteSpace(_authToken))
            request.Headers.TryAddWithoutValidation("X-Sandbox-Worker-Token", _authToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// API-side worker router with retry policy.
/// </summary>
public sealed class RoutedCodeExecutionWorkerClient : ICodeExecutionWorkerClient
{
    private readonly LocalCodeExecutionWorkerClient _local;
    private readonly IReadOnlyList<HttpCodeExecutionWorkerClient> _remotes;
    private readonly SandboxWorkerHealthStateCache _healthCache;
    private readonly bool _preferRemote;
    private readonly bool _fallbackToLocal;
    private readonly int _maxRetries;
    private readonly TimeSpan _retryDelay;
    private readonly int _circuitBreakFailureThreshold;
    private readonly TimeSpan _circuitBreakOpenDuration;
    private readonly ILogger<RoutedCodeExecutionWorkerClient> _logger;
    private int _roundRobinIndex = -1;

    public RoutedCodeExecutionWorkerClient(
        LocalCodeExecutionWorkerClient local,
        IReadOnlyList<HttpCodeExecutionWorkerClient> remotes,
        SandboxWorkerHealthStateCache healthCache,
        bool preferRemote,
        bool fallbackToLocal,
        int maxRetries,
        TimeSpan retryDelay,
        int circuitBreakFailureThreshold,
        TimeSpan circuitBreakOpenDuration,
        ILogger<RoutedCodeExecutionWorkerClient> logger)
    {
        _local = local;
        _remotes = remotes;
        _healthCache = healthCache;
        _preferRemote = preferRemote;
        _fallbackToLocal = fallbackToLocal;
        _maxRetries = Math.Max(0, maxRetries);
        _retryDelay = retryDelay < TimeSpan.Zero ? TimeSpan.Zero : retryDelay;
        _circuitBreakFailureThreshold = Math.Max(1, circuitBreakFailureThreshold);
        _circuitBreakOpenDuration = circuitBreakOpenDuration < TimeSpan.Zero ? TimeSpan.Zero : circuitBreakOpenDuration;
        _logger = logger;
    }

    public async ValueTask<CodeExecutionWorkerResponse> ExecuteAsync(CodeExecutionWorkerRequest request, CancellationToken ct = default)
    {
        CodeExecutionWorkerResponse? last = null;

        for (var attempt = 1; attempt <= _maxRetries + 1; attempt++)
        {
            var current = request with { Attempt = attempt };
            var result = await ExecuteOnceAsync(current, ct).ConfigureAwait(false);
            last = result;

            if (result.Succeeded)
            {
                _logger.LogInformation(
                    "Code worker execution succeeded. WorkerId={WorkerId} LeaseId={LeaseId} SessionId={SessionId} Attempt={Attempt} ExitCode={ExitCode} DurationMs={DurationMs}",
                    result.WorkerId,
                    current.LeaseId,
                    current.SessionId,
                    result.Attempt,
                    result.ExitCode,
                    result.DurationMs);
                return result;
            }

            _logger.LogWarning(
                "Code worker execution failed. WorkerId={WorkerId} LeaseId={LeaseId} SessionId={SessionId} Attempt={Attempt} ErrorCode={ErrorCode} TimedOut={TimedOut}",
                result.WorkerId,
                current.LeaseId,
                current.SessionId,
                result.Attempt,
                result.ErrorCode,
                result.TimedOut);

            if (!ShouldRetry(result) || attempt > _maxRetries) return result;

            _logger.LogWarning(
                "Code worker execution failed with {ErrorCode} on attempt {Attempt}. Retrying...",
                result.ErrorCode,
                attempt);

            if (_retryDelay > TimeSpan.Zero)
                await Task.Delay(_retryDelay, ct).ConfigureAwait(false);
        }

        return last ?? new CodeExecutionWorkerResponse
        {
            Succeeded = false,
            ExitCode = -1,
            ErrorCode = "NO_RESULT",
            ErrorMessage = "worker execution returned no result",
            WorkerId = "router",
            Attempt = request.Attempt,
        };
    }

    private static bool ShouldRetry(CodeExecutionWorkerResponse response)
    {
        // Do not retry business-level execution outcomes (non-zero exit code with Succeeded=true)
        // and do not retry timeout by default to avoid duplicate long-running executions.
        if (response.Succeeded) return false;
        if (response.TimedOut) return false;

        return response.ErrorCode is "WORKER_EXCEPTION" or "HTTP_STATUS" or "EMPTY_RESPONSE";
    }

    private async ValueTask<CodeExecutionWorkerResponse> ExecuteOnceAsync(CodeExecutionWorkerRequest request, CancellationToken ct)
    {
        if (_preferRemote && _remotes.Count == 0)
        {
            _logger.LogWarning(
                "Remote worker mode is enabled but no remote worker client is configured. Falling back to local worker.");
        }

        if (_preferRemote)
        {
            var remote = SelectRemoteTarget(DateTimeOffset.UtcNow);
            if (remote is not null)
            {
                var remoteResult = await remote.ExecuteAsync(request, ct).ConfigureAwait(false);
                if (remoteResult.Succeeded)
                {
                    _healthCache.MarkExecutionSuccess(remote.TargetId);
                    return remoteResult;
                }

                _healthCache.MarkExecutionFailure(
                    remote.TargetId,
                    _circuitBreakFailureThreshold,
                    _circuitBreakOpenDuration,
                    DateTimeOffset.UtcNow);

                if (!_fallbackToLocal) return remoteResult;

                _logger.LogWarning(
                    "Remote worker failed with {ErrorCode}; falling back to local worker. WorkerId={WorkerId} LeaseId={LeaseId} SessionId={SessionId} Attempt={Attempt}",
                    remoteResult.ErrorCode,
                    remote.TargetId,
                    request.LeaseId,
                    request.SessionId,
                    request.Attempt);
            }
        }

        return await _local.ExecuteAsync(request, ct).ConfigureAwait(false);
    }

    private HttpCodeExecutionWorkerClient? SelectRemoteTarget(DateTimeOffset now)
    {
        if (_remotes.Count == 0)
            return null;

        var candidates = _remotes
            .Where(r => _healthCache.IsAvailable(r.TargetId, now))
            .ToList();

        if (candidates.Count == 0)
        {
            _logger.LogWarning("All remote sandbox workers are unavailable by circuit-breaker state.");
            return null;
        }

        var weighted = new List<HttpCodeExecutionWorkerClient>(candidates.Sum(c => c.Weight));
        foreach (var candidate in candidates)
        {
            for (var i = 0; i < candidate.Weight; i++)
                weighted.Add(candidate);
        }

        var next = Interlocked.Increment(ref _roundRobinIndex);
        var index = Math.Abs(next) % weighted.Count;
        return weighted[index];
    }
}
