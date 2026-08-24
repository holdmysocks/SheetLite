using System.Net;

namespace SheetLite.Tests;

internal sealed class UpdateCheckerTests
{
    [Test]
    public void Release_tags_are_parsed_without_v_prefix_or_semver_suffix()
    {
        Assert.True(UpdateChecker.TryParseReleaseVersion("v1.2.3", out Version? simple));
        Assert.Equal(new Version(1, 2, 3), simple);
        Assert.True(UpdateChecker.TryParseReleaseVersion("2.0.1+build.5", out Version? build));
        Assert.Equal(new Version(2, 0, 1), build);
        Assert.False(UpdateChecker.TryParseReleaseVersion("1.2", out _));
        Assert.False(UpdateChecker.TryParseReleaseVersion("1.2.3.4", out _));
        Assert.False(UpdateChecker.TryParseReleaseVersion("latest", out _));
    }

    [Test]
    public void Newer_published_release_is_returned()
    {
        const string json = """
            {
              "tag_name": "v0.7.0",
              "html_url": "https://github.com/holdmysocks/SheetLite/releases/tag/v0.7.0",
              "draft": false,
              "prerelease": false
            }
            """;
        using var client = new HttpClient(new JsonHandler(json));

        AppUpdate? update = UpdateChecker.CheckAsync(new Version(0, 6, 0), client).GetAwaiter().GetResult();

        Assert.NotNull(update);
        Assert.Equal(new Version(0, 7, 0), update!.Version);
        Assert.Equal("v0.7.0", update.TagName);
    }

    [Test]
    public void Current_or_older_release_is_ignored()
    {
        const string json = """
            {
              "tag_name": "v0.6.0",
              "html_url": "https://github.com/holdmysocks/SheetLite/releases/tag/v0.6.0"
            }
            """;
        using var client = new HttpClient(new JsonHandler(json));

        AppUpdate? update = UpdateChecker.CheckAsync(new Version(0, 6, 0), client).GetAwaiter().GetResult();

        Assert.Null(update);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
    }
}
