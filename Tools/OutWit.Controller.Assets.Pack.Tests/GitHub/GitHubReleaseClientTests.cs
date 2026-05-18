using System.Net;
using System.Text;
using OutWit.Controller.Assets.Pack.GitHub;

namespace OutWit.Controller.Assets.Pack.Tests.GitHub
{
    [TestFixture]
    public class GitHubReleaseClientTests
    {
        #region Fields

        private string m_tempDir = null!;

        #endregion

        #region Setup

        [SetUp]
        public void Setup()
        {
            m_tempDir = Path.Combine(Path.GetTempPath(), "outwit-ghc-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_tempDir))
                Directory.Delete(m_tempDir, recursive: true);
        }

        #endregion

        #region Construction

        [Test]
        public void ConstructorRequiresNonEmptyTokenTest()
        {
            using var http = new HttpClient(new StubHandler());

            Assert.That(() => new GitHubReleaseClient(http, "owner", "repo", ""),
                        Throws.InstanceOf<ArgumentException>());
        }

        #endregion

        #region GetReleaseByTag

        [Test]
        public async Task GetReleaseByTagReturnsNullOn404Test()
        {
            var stub = new StubHandler();
            stub.OnRequest = req =>
            {
                Assert.That(req.RequestUri!.AbsolutePath, Is.EqualTo("/repos/owner/repo/releases/tags/v1"));
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            };

            using var http = new HttpClient(stub);
            var client = new GitHubReleaseClient(http, "owner", "repo", "token");

            var info = await client.GetReleaseByTagAsync("v1");

            Assert.That(info, Is.Null);
        }

        [Test]
        public async Task GetReleaseByTagParsesAssetsTest()
        {
            var json = """
                {
                  "id": 42,
                  "tag_name": "v1",
                  "upload_url": "https://uploads.github.com/repos/o/r/releases/42/assets{?name,label}",
                  "assets": [
                    { "id": 100, "name": "a.zip", "size": 1024 },
                    { "id": 101, "name": "b.zip", "size": 2048 }
                  ]
                }
                """;
            using var http = new HttpClient(new StubHandler { Response = JsonResponse(json) });
            var client = new GitHubReleaseClient(http, "o", "r", "token");

            var info = await client.GetReleaseByTagAsync("v1");

            Assert.That(info, Is.Not.Null);
            Assert.That(info!.Id, Is.EqualTo(42));
            Assert.That(info.TagName, Is.EqualTo("v1"));
            Assert.That(info.Assets.Count, Is.EqualTo(2));
            Assert.That(info.Assets[0].Name, Is.EqualTo("a.zip"));
            Assert.That(info.Assets[1].Size, Is.EqualTo(2048));
        }

        #endregion

        #region CreateRelease

        [Test]
        public async Task CreateReleasePostsTagNameAndParsesResponseTest()
        {
            var captured = new List<HttpRequestMessage>();
            var stub = new StubHandler
            {
                OnRequest = req =>
                {
                    captured.Add(req);
                    return JsonResponse("""
                        {
                          "id": 5,
                          "tag_name": "v2",
                          "upload_url": "https://uploads.github.com/repos/o/r/releases/5/assets{?name,label}",
                          "assets": []
                        }
                        """);
                }
            };

            using var http = new HttpClient(stub);
            var client = new GitHubReleaseClient(http, "o", "r", "token");

            var info = await client.CreateReleaseAsync("v2", "v2-name", "body");

            Assert.That(info.Id, Is.EqualTo(5));
            Assert.That(captured.Count, Is.EqualTo(1));
            Assert.That(captured[0].Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(captured[0].RequestUri!.AbsolutePath, Is.EqualTo("/repos/o/r/releases"));

            var body = await captured[0].Content!.ReadAsStringAsync();
            Assert.That(body, Does.Contain("\"tag_name\":\"v2\""));
            Assert.That(body, Does.Contain("\"make_latest\":\"legacy\""));
        }

        #endregion

        #region UploadAsset

        [Test]
        public async Task UploadAssetStripsTemplateSuffixFromUploadUrlTest()
        {
            HttpRequestMessage? captured = null;
            var stub = new StubHandler
            {
                OnRequest = req =>
                {
                    captured = req;
                    return new HttpResponseMessage(HttpStatusCode.Created);
                }
            };

            using var http = new HttpClient(stub);
            var client = new GitHubReleaseClient(http, "o", "r", "token");

            var localFile = Path.Combine(m_tempDir, "payload.bin");
            File.WriteAllBytes(localFile, new byte[] { 1, 2, 3 });

            await client.UploadAssetAsync(
                uploadUrl: "https://uploads.github.com/repos/o/r/releases/5/assets{?name,label}",
                assetName: "payload.bin",
                filePath: localFile);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.RequestUri!.AbsoluteUri,
                        Is.EqualTo("https://uploads.github.com/repos/o/r/releases/5/assets?name=payload.bin"));
            Assert.That(captured.Content!.Headers.ContentType!.MediaType, Is.EqualTo("application/octet-stream"));
        }

        #endregion

        #region EnsureRelease end-to-end

        [Test]
        public async Task EnsureReleaseCreatesAndUploadsWhenTagMissingTest()
        {
            var localFile = Path.Combine(m_tempDir, "x.bin");
            File.WriteAllBytes(localFile, new byte[] { 0xAA });

            var requests = new List<HttpRequestMessage>();
            var stub = new StubHandler
            {
                OnRequest = req =>
                {
                    requests.Add(req);
                    return req.Method.Method switch
                    {
                        // First GET /releases/tags/v1 -> 404 (no release yet)
                        "GET" => new HttpResponseMessage(HttpStatusCode.NotFound),
                        // POST /releases (create) and POST <upload>?name=x.bin (upload)
                        "POST" when req.RequestUri!.Host == "api.github.com" =>
                            JsonResponse("""
                                {
                                  "id": 7,
                                  "tag_name": "v1",
                                  "upload_url": "https://uploads.github.com/repos/o/r/releases/7/assets{?name,label}",
                                  "assets": []
                                }
                                """),
                        "POST" when req.RequestUri!.Host == "uploads.github.com" =>
                            new HttpResponseMessage(HttpStatusCode.Created),
                        _ => new HttpResponseMessage(HttpStatusCode.BadRequest),
                    };
                }
            };

            // Second GET (refresh after upload) needs to return assets list.
            var responseQueue = new Queue<HttpResponseMessage>(new[]
            {
                new HttpResponseMessage(HttpStatusCode.NotFound),  // initial lookup
                JsonResponse("""
                    {
                      "id": 7,
                      "tag_name": "v1",
                      "upload_url": "https://uploads.github.com/repos/o/r/releases/7/assets{?name,label}",
                      "assets": []
                    }
                    """),  // create
                new HttpResponseMessage(HttpStatusCode.Created),   // upload
                JsonResponse("""
                    {
                      "id": 7,
                      "tag_name": "v1",
                      "upload_url": "https://uploads.github.com/repos/o/r/releases/7/assets{?name,label}",
                      "assets": [ { "id": 33, "name": "x.bin", "size": 1 } ]
                    }
                    """),  // refresh
            });
            stub.OnRequest = req =>
            {
                requests.Add(req);
                return responseQueue.Dequeue();
            };

            using var http = new HttpClient(stub);
            var client = new GitHubReleaseClient(http, "o", "r", "token");

            var info = await client.EnsureReleaseAsync(
                tag: "v1", name: "v1", body: "body",
                assets: new[] { ("x.bin", localFile) });

            Assert.That(info.Assets.Count, Is.EqualTo(1));
            Assert.That(info.Assets[0].Name, Is.EqualTo("x.bin"));
            // 4 expected calls: GET, POST create, POST upload, GET refresh.
            Assert.That(requests.Count, Is.EqualTo(4));
        }

        [Test]
        public async Task EnsureReleaseDeletesAndReuploadsExistingAssetsTest()
        {
            var localFile = Path.Combine(m_tempDir, "x.bin");
            File.WriteAllBytes(localFile, new byte[] { 0xBB });

            var requests = new List<HttpRequestMessage>();
            var responseQueue = new Queue<HttpResponseMessage>(new[]
            {
                JsonResponse("""
                    {
                      "id": 9,
                      "tag_name": "v1",
                      "upload_url": "https://uploads.github.com/repos/o/r/releases/9/assets{?name,label}",
                      "assets": [ { "id": 55, "name": "x.bin", "size": 1 } ]
                    }
                    """),                                            // initial lookup
                new HttpResponseMessage(HttpStatusCode.NoContent),  // DELETE asset 55
                new HttpResponseMessage(HttpStatusCode.Created),    // POST upload
                JsonResponse("""
                    {
                      "id": 9,
                      "tag_name": "v1",
                      "upload_url": "https://uploads.github.com/repos/o/r/releases/9/assets{?name,label}",
                      "assets": [ { "id": 56, "name": "x.bin", "size": 1 } ]
                    }
                    """),                                            // refresh
            });
            var stub = new StubHandler
            {
                OnRequest = req =>
                {
                    requests.Add(req);
                    return responseQueue.Dequeue();
                }
            };

            using var http = new HttpClient(stub);
            var client = new GitHubReleaseClient(http, "o", "r", "token");

            await client.EnsureReleaseAsync(
                tag: "v1", name: "v1", body: "body",
                assets: new[] { ("x.bin", localFile) });

            // Verify the DELETE was issued before the upload.
            var methods = requests.Select(r => r.Method.Method).ToList();
            Assert.That(methods, Is.EqualTo(new[] { "GET", "DELETE", "POST", "GET" }));

            // Verify it was the right asset deleted.
            Assert.That(requests[1].RequestUri!.AbsolutePath,
                        Is.EqualTo("/repos/o/r/releases/assets/55"));
        }

        #endregion

        #region URL template stripping

        [Test]
        public void StripUploadUrlTemplateRemovesSuffixTest()
        {
            Assert.That(
                GitHubReleaseClient.StripUploadUrlTemplate(
                    "https://uploads.github.com/repos/o/r/releases/1/assets{?name,label}"),
                Is.EqualTo("https://uploads.github.com/repos/o/r/releases/1/assets"));
        }

        [Test]
        public void StripUploadUrlTemplateLeavesNonTemplatedUrlAloneTest()
        {
            const string Url = "https://uploads.github.com/repos/o/r/releases/1/assets";
            Assert.That(GitHubReleaseClient.StripUploadUrlTemplate(Url), Is.EqualTo(Url));
        }

        #endregion

        #region Stubs

        private static HttpResponseMessage JsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage>? OnRequest { get; set; }

            public HttpResponseMessage? Response { get; set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken ct)
            {
                var resp = OnRequest is not null ? OnRequest(request) : Response;
                return Task.FromResult(resp ?? new HttpResponseMessage(HttpStatusCode.NotImplemented));
            }
        }

        #endregion
    }
}
