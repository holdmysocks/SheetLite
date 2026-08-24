using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SheetLite;

internal sealed record AppUpdate(Version Version, string TagName, Uri ReleasePage);

internal static class UpdateChecker
{
    private const string LatestReleaseApi = "https://api.github.com/repos/holdmysocks/SheetLite/releases/latest";
    private static readonly HttpClient Client = CreateClient();

    internal static Version CurrentVersion =>
        typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);

    internal static Task<AppUpdate?> CheckAsync(CancellationToken cancellationToken = default) =>
        CheckAsync(CurrentVersion, Client, cancellationToken);

    internal static async Task<AppUpdate?> CheckAsync(
        Version currentVersion,
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using HttpResponseMessage response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
        GitHubRelease? release = await JsonSerializer.DeserializeAsync<GitHubRelease>(body, cancellationToken: cancellationToken);
        if (release is null || release.Draft || release.Prerelease ||
            !TryParseReleaseVersion(release.TagName, out Version? releaseVersion) ||
            releaseVersion is null ||
            Normalize(releaseVersion).CompareTo(Normalize(currentVersion)) <= 0 ||
            !TryGetTrustedReleasePage(release.HtmlUrl, out Uri? releasePage) ||
            releasePage is null)
        {
            return null;
        }

        return new AppUpdate(releaseVersion, release.TagName!, releasePage);
    }

    internal static bool TryParseReleaseVersion(string? tagName, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(tagName)) return false;

        string value = tagName.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        int suffix = value.IndexOfAny(['-', '+']);
        if (suffix >= 0) value = value[..suffix];

        return Version.TryParse(value, out version) &&
            version.Major >= 0 && version.Minor >= 0 && version.Build >= 0 && version.Revision < 0;
    }

    private static bool TryGetTrustedReleasePage(string? value, out Uri? page)
    {
        page = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? candidate) ||
            candidate.Scheme != Uri.UriSchemeHttps ||
            !candidate.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !candidate.AbsolutePath.StartsWith("/holdmysocks/SheetLite/releases/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        page = candidate;
        return true;
    }

    private static Version Normalize(Version version) => new(
        version.Major,
        Math.Max(0, version.Minor),
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SheetLite", "update-check"));
        return client;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }
    }
}
