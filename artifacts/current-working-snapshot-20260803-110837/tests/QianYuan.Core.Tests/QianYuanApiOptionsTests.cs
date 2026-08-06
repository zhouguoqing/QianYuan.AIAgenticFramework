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

    [Fact]
    public void Configuration_can_bind_knowledge_store_settings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["QianYuan:KnowledgeStore:Provider"] = "postgres",
                ["QianYuan:KnowledgeStore:Postgres:ConnectionString"] = "Host=localhost;Database=qianyuan;Username=test;Password=test;",
                ["QianYuan:KnowledgeStore:Postgres:TableName"] = "knowledge_documents",
            })
            .Build();

        var options = configuration.GetSection("QianYuan").Get<QianYuanApiOptions>();

        options.Should().NotBeNull();
        options!.KnowledgeStore.Provider.Should().Be("postgres");
        options.KnowledgeStore.Postgres.ConnectionString.Should().Be("Host=localhost;Database=qianyuan;Username=test;Password=test;");
        options.KnowledgeStore.Postgres.TableName.Should().Be("knowledge_documents");
    }
}