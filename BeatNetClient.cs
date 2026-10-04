using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BEATNET;

internal sealed class BeatNetClient : IDisposable
{
    private readonly Uri server;
    private readonly HttpClient http;
    private static readonly JsonSerializerSettings JsonSettings = new() { MaxDepth = 16 };

    internal BeatNetClient(string address, HttpMessageHandler? handler = null)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https") || uri.AbsolutePath != "/"
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new InvalidDataException("ServerUrl must be an HTTP or HTTPS address without a path");
        }

        server = uri;
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
        http.Timeout = Timeout.InfiniteTimeSpan;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BEATNET/1.0.0");
    }

    internal async Task<CatalogPage> List(string query, int offset, int limit, CancellationToken token)
    {
        var path = $"/api/beatmaps?query={Uri.EscapeDataString(query)}&offset={offset}&limit={limit}";
        var page = await ReadJson<CatalogPage>(path, token).ConfigureAwait(false);
        if (page.Items == null || page.Items.Length > limit || page.Total < 0 || page.Offset != offset || page.Limit != limit)
        {
            throw new InvalidDataException("The server returned an invalid beatmap list");
        }
        foreach (var beatmap in page.Items)
        {
            ValidateBeatmap(beatmap);
        }
        return page;
    }

    internal async Task<BeatmapEntry> Get(string id, CancellationToken token)
    {
        if (!IsId(id))
        {
            throw new InvalidDataException("Invalid beatmap ID");
        }
        var beatmap = await ReadJson<BeatmapEntry>($"/api/beatmaps/{id}", token).ConfigureAwait(false);
        ValidateBeatmap(beatmap);
        if (beatmap.Id != id || beatmap.Files == null || beatmap.Files.Length == 0 || beatmap.Files.Length > 100)
        {
            throw new InvalidDataException("The server returned invalid beatmap details");
        }
        foreach (var file in beatmap.Files)
        {
            var path = $"/api/beatmaps/{id}/revisions/{beatmap.Revision.Id}/files/{file?.Id}";
            if (file == null || (!IsId(file.Id) && !Regex.IsMatch(file.Id ?? "", @"^\d{17,20}$"))
                || file.Url != path || file.Size <= 0 || !Regex.IsMatch(file.Sha256 ?? "", "^[0-9a-f]{64}$"))
            {
                throw new InvalidDataException("The server returned invalid download details");
            }
        }
        return beatmap;
    }

    internal static bool IsId(string? value) => Guid.TryParseExact(value, "D", out var id) && id.ToString("D") == value;

    internal Uri PreviewUrl(BeatmapEntry beatmap)
    {
        var path = $"/api/beatmaps/{beatmap.Id}/revisions/{beatmap.Revision.Id}/preview";
        if (beatmap.Preview == null || beatmap.Preview.Url != path || float.IsNaN(beatmap.Preview.Start)
            || float.IsInfinity(beatmap.Preview.Start) || beatmap.Preview.Start < 0f)
        {
            throw new InvalidDataException("The server returned invalid preview details");
        }
        return new Uri(server, path);
    }

    private static void ValidateBeatmap(BeatmapEntry? beatmap)
    {
        if (beatmap == null || !IsId(beatmap.Id) || beatmap.Revision == null
            || !IsId(beatmap.Revision.Id) || beatmap.Revision.Number < 1)
        {
            throw new InvalidDataException("The server returned invalid beatmap metadata");
        }
    }

    private async Task<T> ReadJson<T>(string path, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await GetResponse(path, timeout.Token).ConfigureAwait(false);
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + count > 1_048_576)
            {
                throw new InvalidDataException("The server response is too large");
            }
            bytes.Write(buffer, 0, count);
        }
        try
        {
            return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes.ToArray()), JsonSettings)
                ?? throw new InvalidDataException("The server returned an empty response");
        }
        catch (JsonException)
        {
            throw new InvalidDataException("The server returned invalid JSON");
        }
    }

    internal async Task Download(BeatmapFile file, string path, Action<long> progress, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var response = await GetResponse(file.Url, timeout.Token).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is long length && length != file.Size)
        {
            throw new InvalidDataException("The download size does not match the beatmap");
        }
        using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long received = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) != 0)
        {
            received += count;
            if (received > file.Size)
            {
                throw new InvalidDataException("The download exceeds the expected size");
            }
            hash.AppendData(buffer, 0, count);
            await destination.WriteAsync(buffer, 0, count, timeout.Token).ConfigureAwait(false);
            progress(received);
        }
        var checksum = BitConverter.ToString(hash.GetHashAndReset()).Replace("-", "").ToLowerInvariant();
        if (received != file.Size || checksum != file.Sha256)
        {
            throw new InvalidDataException("The download failed its integrity check");
        }
        await destination.FlushAsync(timeout.Token).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> GetResponse(string path, CancellationToken token)
    {
        var response = await http.GetAsync(new Uri(server, path), HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }
        var status = response.StatusCode;
        response.Dispose();
        if (status == HttpStatusCode.NotFound)
        {
            throw new InvalidDataException("This beatmap is no longer available");
        }
        if (status == HttpStatusCode.ServiceUnavailable)
        {
            throw new InvalidDataException("The BEATNET server is not ready");
        }
        throw new HttpRequestException($"The server returned HTTP {(int)status}");
    }

    public void Dispose() => http.Dispose();
}
