using FluentAssertions;
using QianYuan.Api.Configuration;
using QianYuan.Api.Controllers;
using QianYuan.Providers.OpenAICompat;
using Xunit;

namespace QianYuan.Core.Tests;

public class ImageGenerationTests
{
    [Fact]
    public void GetCandidateModels_ForGptImage2_ShouldIncludeFallbackGptImage1()
    {
        var req = new ImageGenerationRequest { Model = "gpt-image-2" };
        var provider = new OpenAIProviderOptions { ImageModel = "gpt-image-1" };

        var candidates = ImageGenerationModelResolver.GetCandidateModels(req, provider);

        candidates.Should().Equal("gpt-image-2", "gpt-image-1");
    }

    [Fact]
    public void GetCandidateModels_ForNonGptImageModel_ShouldReturnSingleModel()
    {
        var req = new ImageGenerationRequest { Model = "gpt-4o-mini" };
        var provider = new OpenAIProviderOptions();

        var candidates = ImageGenerationModelResolver.GetCandidateModels(req, provider);

        candidates.Should().Equal("gpt-4o-mini");
    }

    [Fact]
    public void ResolveChatModel_ForGptImage2_ShouldFallbackToDefaultChatModel()
    {
        var resolved = OpenAICompatModelResolver.ResolveChatModel("gpt-image-2", "gpt-5.5");

        resolved.Should().Be("gpt-5.5");
    }

    [Fact]
    public void ResolveChatModel_ForRegularModel_ShouldKeepRequestedModel()
    {
        var resolved = OpenAICompatModelResolver.ResolveChatModel("gpt-4o-mini", "gpt-5.5");

        resolved.Should().Be("gpt-4o-mini");
    }
}
