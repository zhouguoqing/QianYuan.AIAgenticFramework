using FluentAssertions;
using QianYuan.Kernel.Skills;

namespace QianYuan.Core.Tests;

public class MarkdownSkillLoaderTests
{
    [Fact]
    public void LoadFromFile_reads_common_skill_frontmatter()
    {
        using var dir = new TempDirectory();
        var skillDir = Path.Combine(dir.Path, "code-review");
        Directory.CreateDirectory(skillDir);
        var file = Path.Combine(skillDir, "SKILL.md");
        File.WriteAllText(file, """
---
name: code-review
description: Review code for bugs and missing tests
tags: [review, testing]
---

# Code Review

Find behavioral regressions before style issues.
""");

        var skill = MarkdownSkillLoader.LoadFromFile(file, dir.Path, "agent");

        skill.Id.Should().Be("agent.code.review");
        skill.Name.Should().Be("code-review");
        skill.Description.Should().Be("Review code for bugs and missing tests");
        skill.Tags.Should().Equal("review", "testing");
        skill.SystemPromptFragment.Should().Contain("Find behavioral regressions");
    }

    [Fact]
    public void LoadFromDirectory_scans_skill_md_recursively()
    {
        using var dir = new TempDirectory();
        var first = Path.Combine(dir.Path, "skills", "summarize");
        var second = Path.Combine(dir.Path, "skills", "pdf");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "Skill.md"), "---\nname: summarize\ndescription: Summarize text\n---\nSummarize documents.");
        File.WriteAllText(Path.Combine(second, "SKILL.md"), "---\nname: pdf\ndescription: Work with PDFs\n---\nHandle PDF tasks.");

        var skills = MarkdownSkillLoader.LoadFromDirectory(Path.Combine(dir.Path, "skills"), recursive: true, idPrefix: "catalog");

        skills.Select(s => s.Id).Should().BeEquivalentTo("catalog.pdf", "catalog.summarize");
    }

    [Fact]
    public void LoadFromDirectory_loads_repository_sample_skills()
    {
        var samplesPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "skills"));

        var skills = MarkdownSkillLoader.LoadFromDirectory(samplesPath, recursive: true, idPrefix: "sample");

        skills.Select(s => s.Id).Should().Contain([
            "sample.api.design",
            "sample.code.review",
            "sample.debugging",
            "sample.docs.writing",
            "sample.requirements.analysis",
        ]);
        skills.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.SystemPromptFragment));
    }

    [Fact]
    public void LoadFromDirectory_loads_project_agent_skills_from_skills_sh()
    {
        var agentSkillsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".agents", "skills"));

        var skills = MarkdownSkillLoader.LoadFromDirectory(agentSkillsPath, recursive: true, idPrefix: "agent");

        skills.Select(s => s.Id).Should().Contain([
            "agent.brainstorm",
            "agent.brainstorming",
            "agent.find.skills",
            "agent.pdf",
            "agent.skill.creator",
            "agent.summarize",
            "agent.using.superpowers",
        ]);
        skills.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.SystemPromptFragment));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qianyuan-skills-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}