using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace OutWit.Controller.Assets.Pack.GitHub
{
    /// <summary>
    /// Minimal GitHub REST API client scoped to release management. Talks to
    /// api.github.com and uploads.github.com only. Created with a PAT (which
    /// needs at least 'contents:write' on the target repo) and reused across
    /// multiple operations.
    /// </summary>
    /// <remarks>
    /// Idempotency: <see cref="EnsureReleaseAsync"/> creates the release if it
    /// doesn't exist; if it does, every same-named existing asset is deleted
    /// and replaced with the local one. Other assets on the release are left
    /// alone (we don't want to wipe historical files an author added manually).
    /// </remarks>
    public sealed class GitHubReleaseClient
    {
        #region Constants

        public const string DefaultApiBase = "https://api.github.com";

        private const string UserAgent = "outwit-assets-pack";

        private const string GitHubAcceptHeader = "application/vnd.github+json";

        private const string GitHubApiVersion = "2022-11-28";

        // The upload_url returned by GitHub looks like
        //   https://uploads.github.com/repos/{owner}/{repo}/releases/{id}/assets{?name,label}
        // and we have to strip the trailing URI template before appending ?name=...
        private const string UploadUrlTemplateSuffix = "{?name,label}";

        #endregion

        #region Fields

        private readonly HttpClient m_http;

        private readonly string m_owner;

        private readonly string m_repo;

        #endregion

        #region Constructor

        public GitHubReleaseClient(HttpClient http, string owner, string repo, string token, string? apiBase = null)
        {
            if (http is null) throw new ArgumentNullException(nameof(http));
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("owner is empty.", nameof(owner));
            if (string.IsNullOrWhiteSpace(repo))  throw new ArgumentException("repo is empty.",  nameof(repo));
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("token is empty.", nameof(token));

            m_http = http;
            m_owner = owner;
            m_repo = repo;

            if (m_http.BaseAddress is null)
                m_http.BaseAddress = new Uri(apiBase ?? DefaultApiBase);

            ApplyDefaultHeaders(m_http, token);
        }

        private static void ApplyDefaultHeaders(HttpClient http, string token)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (!http.DefaultRequestHeaders.UserAgent.Any())
                http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            if (!http.DefaultRequestHeaders.Accept.Any())
                http.DefaultRequestHeaders.Accept.ParseAdd(GitHubAcceptHeader);
            if (!http.DefaultRequestHeaders.Contains("X-GitHub-Api-Version"))
                http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", GitHubApiVersion);
        }

        #endregion

        #region API

        /// <summary>
        /// Fetch a release by tag. Returns null if the tag doesn't exist.
        /// </summary>
        public async Task<ReleaseInfo?> GetReleaseByTagAsync(string tag, CancellationToken ct = default)
        {
            var url = $"/repos/{m_owner}/{m_repo}/releases/tags/{Uri.EscapeDataString(tag)}";
            using var response = await m_http.GetAsync(url, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

            var dto = await response.Content.ReadFromJsonAsync<ReleaseDto>(cancellationToken: ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Empty release response for tag '{tag}'.");

            return ReleaseInfoFrom(dto);
        }

        /// <summary>
        /// Create a new release at <paramref name="tag"/>.
        /// </summary>
        public async Task<ReleaseInfo> CreateReleaseAsync(string tag, string name, string body, CancellationToken ct = default)
        {
            var url = $"/repos/{m_owner}/{m_repo}/releases";
            var payload = new CreateReleaseDto(tag, name, body, MakeLatest: "legacy");
            using var response = await m_http.PostAsJsonAsync(url, payload, ct).ConfigureAwait(false);

            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

            var dto = await response.Content.ReadFromJsonAsync<ReleaseDto>(cancellationToken: ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Empty release response after creating tag '{tag}'.");

            return ReleaseInfoFrom(dto);
        }

        /// <summary>
        /// Delete a release asset by its API id.
        /// </summary>
        public async Task DeleteAssetAsync(long assetId, CancellationToken ct = default)
        {
            var url = $"/repos/{m_owner}/{m_repo}/releases/assets/{assetId}";
            using var response = await m_http.DeleteAsync(url, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Upload a single file as a release asset. <paramref name="uploadUrl"/> is the
        /// template returned in the release payload (with or without the
        /// '{?name,label}' suffix — both shapes accepted).
        /// </summary>
        public async Task UploadAssetAsync(
            string uploadUrl,
            string assetName,
            string filePath,
            CancellationToken ct = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Asset file not found: '{filePath}'.", filePath);

            var endpoint = StripUploadUrlTemplate(uploadUrl) + $"?name={Uri.EscapeDataString(assetName)}";

            await using var stream = File.OpenRead(filePath);
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Headers.ContentLength = stream.Length;

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
            using var response = await m_http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Ensure the release for <paramref name="tag"/> exists and every file in
        /// <paramref name="assets"/> is uploaded. Existing same-named assets are
        /// deleted and replaced. Other assets are left untouched.
        /// </summary>
        public async Task<ReleaseInfo> EnsureReleaseAsync(
            string tag,
            string name,
            string body,
            IEnumerable<(string AssetName, string FilePath)> assets,
            IProgress<string>? log = null,
            CancellationToken ct = default)
        {
            var release = await GetReleaseByTagAsync(tag, ct).ConfigureAwait(false);
            if (release is null)
            {
                log?.Report($"Creating release '{tag}'...");
                release = await CreateReleaseAsync(tag, name, body, ct).ConfigureAwait(false);
            }
            else
            {
                log?.Report($"Reusing existing release '{tag}' (id={release.Id}, {release.Assets.Count} asset(s) on it).");
            }

            var existingByName = release.Assets.ToDictionary(a => a.Name, a => a.Id, StringComparer.Ordinal);

            foreach (var (assetName, filePath) in assets)
            {
                if (existingByName.TryGetValue(assetName, out var existingId))
                {
                    log?.Report($"  replace {assetName} (deleting existing asset id={existingId})...");
                    await DeleteAssetAsync(existingId, ct).ConfigureAwait(false);
                }

                log?.Report($"  upload  {assetName} ({new FileInfo(filePath).Length:N0} bytes)...");
                await UploadAssetAsync(release.UploadUrl, assetName, filePath, ct).ConfigureAwait(false);
            }

            // Refresh to pick up the newly-uploaded asset ids.
            return await GetReleaseByTagAsync(tag, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Release '{tag}' disappeared after upload — race condition or revoked permission?");
        }

        #endregion

        #region Tools

        public static string StripUploadUrlTemplate(string uploadUrl)
        {
            return uploadUrl.EndsWith(UploadUrlTemplateSuffix, StringComparison.Ordinal)
                ? uploadUrl[..^UploadUrlTemplateSuffix.Length]
                : uploadUrl;
        }

        private static ReleaseInfo ReleaseInfoFrom(ReleaseDto dto)
        {
            return new ReleaseInfo
            {
                Id = dto.Id,
                TagName = dto.TagName,
                UploadUrl = dto.UploadUrl,
                Assets = dto.Assets.Select(a => new ReleaseAsset
                {
                    Id = a.Id,
                    Name = a.Name,
                    Size = a.Size,
                }).ToList(),
            };
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
        {
            if (response.IsSuccessStatusCode)
                return;

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException(
                $"GitHub API request failed: {(int)response.StatusCode} {response.ReasonPhrase}. " +
                $"URL: {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}. " +
                $"Body: {Trim(body, 500)}");
        }

        private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "...";

        #endregion

        #region DTOs

        private sealed record CreateReleaseDto(
            [property: JsonPropertyName("tag_name")] string TagName,
            [property: JsonPropertyName("name")]     string Name,
            [property: JsonPropertyName("body")]     string Body,
            [property: JsonPropertyName("make_latest")] string MakeLatest);

        private sealed record ReleaseDto(
            [property: JsonPropertyName("id")]         long Id,
            [property: JsonPropertyName("tag_name")]   string TagName,
            [property: JsonPropertyName("upload_url")] string UploadUrl,
            [property: JsonPropertyName("assets")]     List<AssetDto> Assets);

        private sealed record AssetDto(
            [property: JsonPropertyName("id")]   long Id,
            [property: JsonPropertyName("name")] string Name,
            [property: JsonPropertyName("size")] long Size);

        #endregion
    }

    public sealed class ReleaseInfo
    {
        public required long Id { get; init; }

        public required string TagName { get; init; }

        public required string UploadUrl { get; init; }

        public required IReadOnlyList<ReleaseAsset> Assets { get; init; }
    }

    public sealed class ReleaseAsset
    {
        public required long Id { get; init; }

        public required string Name { get; init; }

        public required long Size { get; init; }
    }
}
