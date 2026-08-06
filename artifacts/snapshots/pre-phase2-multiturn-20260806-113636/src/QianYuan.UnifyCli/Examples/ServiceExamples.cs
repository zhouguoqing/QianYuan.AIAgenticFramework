using System.Text.Json;
using QianYuan.UnifyCli.Implementation;
using QianYuan.UnifyCli.Abstractions;

namespace QianYuan.UnifyCli.Examples;

/// <summary>
/// Example: Weather Service CLI
/// Demonstrates how to wrap a weather API as a CLI service with multiple methods.
/// </summary>
public sealed class WeatherServiceExample
{
    /// <summary>
    /// Creates an example CLI service for OpenWeatherMap API.
    /// </summary>
    public static CliServiceDefinition CreateWeatherService()
    {
        var service = new CliServiceDefinition
        {
            Id = "weather.openweathermap",
            Name = "OpenWeatherMap",
            Description = "Get weather information using OpenWeatherMap API",
            BaseUri = "https://api.openweathermap.org",
            Tags = new[] { "weather", "forecast", "climate" }
        };

        // Current weather method
        var currentWeatherMethod = new CliMethodDefinition
        {
            Id = "get_current_weather",
            Name = "Get Current Weather",
            Description = "Get current weather for a city",
            BaseUri = "https://api.openweathermap.org",
            HttpMethod = "GET",
            PathTemplate = "/data/2.5/weather",
            ParametersSchema = JsonSerializer.Serialize(new
            {
                type = "object",
                properties = new
                {
                    city = new { type = "string", description = "City name" },
                    units = new { type = "string", description = "Unit system (metric, imperial). Default: metric" }
                },
                required = new[] { "city" }
            }),
            ResponseSchema = JsonSerializer.Serialize(new { type = "object" }),
            QueryParams = new Dictionary<string, string>
            {
                { "q", "$city" },
                { "units", "$units" },
                { "appid", "${OPENWEATHER_API_KEY}" } // Would be replaced at runtime
            },
            Tags = new[] { "current", "weather" },
            TimeoutMs = 10000,
            RetryCount = 2
        };

        // Forecast method
        var forecastMethod = new CliMethodDefinition
        {
            Id = "get_forecast",
            Name = "Get Weather Forecast",
            Description = "Get 5-day weather forecast for a city",
            BaseUri = "https://api.openweathermap.org",
            HttpMethod = "GET",
            PathTemplate = "/data/2.5/forecast",
            ParametersSchema = JsonSerializer.Serialize(new
            {
                type = "object",
                properties = new
                {
                    city = new { type = "string", description = "City name" },
                    units = new { type = "string", description = "Unit system (metric, imperial). Default: metric" }
                },
                required = new[] { "city" }
            }),
            ResponseSchema = JsonSerializer.Serialize(new { type = "object" }),
            QueryParams = new Dictionary<string, string>
            {
                { "q", "$city" },
                { "units", "$units" }
            },
            Tags = new[] { "forecast", "weather" },
            TimeoutMs = 10000,
            RetryCount = 2
        };

        service.RegisterMethods(currentWeatherMethod, forecastMethod);
        return service;
    }
}

/// <summary>
/// Example: GitHub API CLI
/// Demonstrates repository-related operations via GitHub REST API.
/// </summary>
public sealed class GitHubServiceExample
{
    public static CliServiceDefinition CreateGitHubService(string? gitHubToken = null)
    {
        var service = new CliServiceDefinition
        {
            Id = "vcs.github",
            Name = "GitHub API",
            Description = "Interact with GitHub repositories and data",
            BaseUri = "https://api.github.com",
            Tags = new[] { "github", "vcs", "repository" }
        };

        // Apply authentication if token is provided
        if (!string.IsNullOrEmpty(gitHubToken))
        {
            var authOptions = new AuthenticationOptions
            {
                Type = "bearer",
                Token = gitHubToken
            };
            var authFactory = new AuthenticationProviderFactory();
            service.DefaultAuthenticationProvider = authFactory.Create(authOptions);
        }
        else
        {
            // Empty block to satisfy C# syntax
        }

        // Get repository info method
        var getRepoMethod = new CliMethodDefinition
        {
            Id = "get_repository",
            Name = "Get Repository Information",
            Description = "Get information about a GitHub repository",
            BaseUri = "https://api.github.com",
            HttpMethod = "GET",
            PathTemplate = "/repos/{owner}/{repo}",
            ParametersSchema = JsonSerializer.Serialize(new
            {
                type = "object",
                properties = new
                {
                    owner = new { type = "string", description = "Repository owner username" },
                    repo = new { type = "string", description = "Repository name" }
                },
                required = new[] { "owner", "repo" }
            }),
            RequestHeaders = new Dictionary<string, string>
            {
                { "Accept", "application/vnd.github.v3+json" }
            },
            Tags = new[] { "repository", "info" }
        };

        // List issues method
        var listIssuesMethod = new CliMethodDefinition
        {
            Id = "list_issues",
            Name = "List Repository Issues",
            Description = "List issues in a GitHub repository",
            BaseUri = "https://api.github.com",
            HttpMethod = "GET",
            PathTemplate = "/repos/{owner}/{repo}/issues",
            ParametersSchema = JsonSerializer.Serialize(new
            {
                type = "object",
                properties = new
                {
                    owner = new { type = "string", description = "Repository owner" },
                    repo = new { type = "string", description = "Repository name" },
                    state = new { type = "string", description = "Filter by state (open, closed, all). Default: open" },
                    per_page = new { type = "integer", description = "Items per page. Default: 30" }
                },
                required = new[] { "owner", "repo" }
            }),
            QueryParams = new Dictionary<string, string>
            {
                { "state", "$state" },
                { "per_page", "$per_page" }
            },
            RequestHeaders = new Dictionary<string, string>
            {
                { "Accept", "application/vnd.github.v3+json" }
            },
            Tags = new[] { "issues", "list" }
        };

        service.RegisterMethods(getRepoMethod, listIssuesMethod);
        return service;
    }
}

/// <summary>
/// Example: Slack API CLI
/// Demonstrates message posting and channel operations.
/// </summary>
public sealed class SlackServiceExample
{
    public static CliServiceDefinition CreateSlackService(string? slackToken = null)
    {
        var service = new CliServiceDefinition
        {
            Id = "messaging.slack",
            Name = "Slack API",
            Description = "Send messages and manage Slack channels",
            BaseUri = "https://slack.com/api",
            Tags = new[] { "slack", "messaging", "notification" }
        };

        // Apply authentication
        if (!string.IsNullOrEmpty(slackToken))
        {
            var authOptions = new AuthenticationOptions
            {
                Type = "bearer",
                Token = slackToken
            };
            var authFactory = new AuthenticationProviderFactory();
            service.DefaultAuthenticationProvider = authFactory.Create(authOptions);
        }

        // Post message method
        var postMessageMethod = new CliMethodDefinition
        {
            Id = "post_message",
            Name = "Post Message",
            Description = "Send a message to a Slack channel",
            BaseUri = "https://slack.com/api",
            HttpMethod = "POST",
            PathTemplate = "/chat.postMessage",
            ParametersSchema = JsonSerializer.Serialize(new
            {
                type = "object",
                properties = new
                {
                    channel = new { type = "string", description = "Channel ID or name" },
                    text = new { type = "string", description = "Message text" },
                    thread_ts = new { type = "string", description = "Optional: thread timestamp for threaded replies" }
                },
                required = new[] { "channel", "text" }
            }),
            RequestBodyTemplate = "$.",  // Send entire parameters as JSON body
            RequestHeaders = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" }
            },
            Tags = new[] { "message", "post" },
            TimeoutMs = 5000
        };

        service.RegisterMethods(postMessageMethod);
        return service;
    }
}
