using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SmartHunter.Core.Updates
{
    public sealed class GitHubUpdateClient : IDisposable
    {
        readonly HttpClient client;
        public GitHubUpdateClient(HttpMessageHandler handler = null)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SmartHunter-Unified/" + typeof(GitHubUpdateClient).Assembly.GetName().Version);
            client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        }
        public async Task<UpdatePackage> Check(string url, Version current, CancellationToken token)
        {
            var source = UpdateSource.Parse(url);
            string repository = source.Repository, branch = source.Branch;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                // Pin one commit so a concurrent push cannot mix different builds.
                string reference = "https://api.github.com/repos/" + repository + "/git/ref/heads/" + String.Join("/", branch.Split('/').Select(Uri.EscapeDataString));
                var info = JObject.Parse(await Text(new Uri(reference), 65536, timeout.Token).ConfigureAwait(false));
                string revision = (string)info["object"]?["sha"];
                if ((string)info["ref"] != "refs/heads/" + branch || (string)info["object"]?["type"] != "commit" || !UpdatePackage.ValidRevision(revision)) throw new InvalidDataException("Invalid GitHub branch reference.");
                var location = new UpdatePackage { Repository = repository, Revision = revision };
                string json = await Text(location.FileUri(UpdatePackage.ManifestName), 65536, timeout.Token).ConfigureAwait(false);
                var package = UpdatePackage.Parse(json);
                package.Repository = repository; package.Branch = branch; package.Revision = revision;
                return package.Version > current ? package : null;
            }
        }
        async Task<string> Text(Uri uri, long maximum, CancellationToken token)
        {
            using (var output = new MemoryStream())
            {
                await Fetch(uri, output, maximum, null, token).ConfigureAwait(false);
                return Encoding.UTF8.GetString(output.ToArray()).TrimStart('\uFEFF');
            }
        }
        public async Task<string> Download(UpdatePackage package, CancellationToken token, string installedDirectory = null)
        {
            var verified = UpdatePackage.Parse(package.ManifestJson);
            verified.Repository = package.Repository; verified.Branch = package.Branch; verified.Revision = package.Revision;
            verified.FileUri(UpdatePackage.ManifestName);
            string workspace = UpdateFiles.NewWorkspace(), target = Path.Combine(workspace, "incoming"); Directory.CreateDirectory(target);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(120));
                foreach (string name in UpdatePackage.PayloadFiles)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    string output = Path.Combine(target, name.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(output));
                    bool reused = false;
                    if (!String.IsNullOrEmpty(installedDirectory))
                    {
                        string existing = Path.Combine(installedDirectory, name.Replace('/', Path.DirectorySeparatorChar));
                        try
                        {
                            UpdateFiles.RejectLinks(existing);
                            if (File.Exists(existing) && new FileInfo(existing).Length == verified.Sizes[name] && UpdateFiles.Hash(existing) == verified.Files[name])
                            { File.Copy(existing, output); reused = true; }
                        }
                        catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                    if (!reused && File.Exists(output)) File.Delete(output);
                    if (!reused)
                        using (var file = new FileStream(output, FileMode.CreateNew))
                            await Fetch(verified.FileUri(name), file, verified.Sizes[name], verified.Sizes[name], timeout.Token).ConfigureAwait(false);
                    if (new FileInfo(output).Length != verified.Sizes[name] || UpdateFiles.Hash(output) != verified.Files[name]) throw new InvalidDataException("Update file checksum mismatch: " + name);
                }
                File.WriteAllText(Path.Combine(target, UpdatePackage.ManifestName), verified.ManifestJson, new UTF8Encoding(false));
                UpdateFiles.Validate(target, verified.Version);
                return target;
            }
        }
        async Task Fetch(Uri uri, Stream output, long maximum, long? exactSize, CancellationToken token)
        {
            if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" || uri.Host != "api.github.com" && uri.Host != "raw.githubusercontent.com") throw new InvalidDataException("Unexpected GitHub download origin.");
            using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound) throw new UpdateSourceMissingException(uri.AbsolutePath);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long length && (length > maximum || exactSize.HasValue && length != exactSize.Value)) throw new InvalidDataException("Update response length mismatch.");
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var buffer = new byte[65536]; long count = 0; int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                    { count += read; if (count > maximum) throw new InvalidDataException("Update response exceeds its size limit."); await output.WriteAsync(buffer, 0, read, token).ConfigureAwait(false); }
                    if (exactSize.HasValue && count != exactSize.Value) throw new InvalidDataException("Incomplete update download.");
                }
            }
        }
        public void Dispose() => client.Dispose();
    }
}
