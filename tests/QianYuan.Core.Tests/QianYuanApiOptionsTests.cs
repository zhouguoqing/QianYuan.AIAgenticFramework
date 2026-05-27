using FluentAssertions;
using Microsoft.Extensions.Configuration;
using QianYuan.Api.Configuration;
using QianYuan.Kernel.Agents;
using QianYuan.Kernel.ReAct;

namespace QianYuan.Core.Tests;

public class QianYuanApiOptionsTests
{
    [Fact]
    public void Default_max_iterations_is_100()
    {
        new QianYuanApiOptions().DefaultAgentMaxIterations.Should().Be(100);
        new ReActAgentDefinition
        {
            Id = "agent",
            Name = "Agent",
            Description = "Test agent",
        }.MaxIterations.Should().Be(100);
        new ReActEngineOptions().MaxIterations.Should().Be(100);
    }

    [Fact]
    public void Configuration_can_override_default_agent_max_iterations()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["QianYuan:DefaultAgentMaxIterations"] = "37",
            })
            .Build();

        var options = configuration.GetSection("QianYuan").Get<QianYuanApiOptions>();

        options.Should().NotBeNull();
        options!.DefaultAgentMaxIterations.Should().Be(37);
    }
}