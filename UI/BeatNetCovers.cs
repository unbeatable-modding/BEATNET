extern alias GameDrawing;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Drawing = GameDrawing::System.Drawing;

namespace BEATNET;

internal sealed class BeatNetCovers : IDisposable
{
    private readonly string cache;
    private readonly BeatNetClient client = new("http://92.5.175.72");
    private readonly Dictionary<string, Texture2D?> textures = new();
    private readonly Dictionary<string, Task<byte[]?>> pending = new();
    private CancellationTokenSource cancellation = new();

    internal BeatNetCovers(string dataPath) => cache = Path.Combine(dataPath, "BEATNET_data", "covers");

    internal Texture2D? Get(BeatmapEntry entry)
    {
        var local = entry.CoverPath;
        if (local == null && entry.Cover == null)
        {
            return null;
        }
        var key = local == null ? entry.Id + "-" + entry.Revision.Id
            : "local:" + local + "|" + entry.Revision.Id + "|" + entry.CoverStamp;
        if (textures.TryGetValue(key, out var texture))
        {
            return texture;
        }
        if (!pending.ContainsKey(key) && pending.Count < 2)
        {
            var token = cancellation.Token;
            pending[key] = Task.Run(async () =>
            {
                try
                {
                    Directory.CreateDirectory(cache);
                    var name = key;
                    if (local != null)
                    {
                        using var hash = SHA256.Create();
                        name = "local-v1-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").ToLowerInvariant();
                    }
                    var path = Path.Combine(cache, name + ".jpg");
                    if (File.Exists(path))
                    {
                        var bytes = Read(path, 98304, token);
                        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                        return bytes;
                    }
                    var downloaded = local != null ? Resize(local, token)
                        : await client.Cover(entry, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    Save(path, downloaded, token);
                    try
                    {
                        Trim();
                    }
                    catch (IOException)
                    {
                    }
                    return downloaded;
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (Exception)
                {
                    return null;
                }
            }, token);
        }
        return null;
    }

    private static byte[] Read(string path, long limit, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (source.Length <= 0 || source.Length > limit)
        {
            throw new InvalidDataException("Invalid cover size");
        }
        var bytes = new byte[(int)source.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            var count = source.Read(bytes, offset, bytes.Length - offset);
            if (count == 0)
            {
                throw new EndOfStreamException();
            }
            offset += count;
        }
        return bytes;
    }

    private static byte[] Resize(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length <= 0 || stream.Length > 33_554_432)
        {
            throw new InvalidDataException("Invalid cover size");
        }
        using var original = Drawing.Image.FromStream(stream, false, true);
        if ((long)original.Width * original.Height > 16_777_216)
        {
            throw new InvalidDataException("Invalid cover dimensions");
        }
        var scale = Math.Min(1d, 512d / Math.Max(original.Width, original.Height));
        var width = Math.Max(1, (int)Math.Round(original.Width * scale));
        var height = Math.Max(1, (int)Math.Round(original.Height * scale));
        using var image = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var graphics = Drawing.Graphics.FromImage(image))
        {
            graphics.Clear(Drawing.Color.Black);
            graphics.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(original, new Drawing.Rectangle(0, 0, width, height));
        }
        var codec = Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
            .First(item => item.FormatID == Drawing.Imaging.ImageFormat.Jpeg.Guid);
        foreach (var quality in new long[] { 75, 55, 35 })
        {
            token.ThrowIfCancellationRequested();
            using var bytes = new MemoryStream();
            using var parameters = new Drawing.Imaging.EncoderParameters(1);
            parameters.Param[0] = new Drawing.Imaging.EncoderParameter(Drawing.Imaging.Encoder.Quality, quality);
            image.Save(bytes, codec, parameters);
            if (bytes.Length <= 98304)
            {
                return bytes.ToArray();
            }
        }
        throw new InvalidDataException("The cover is too large");
    }

    private static void Save(string path, byte[] bytes, CancellationToken token)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            token.ThrowIfCancellationRequested();
            if (!File.Exists(path))
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private void Trim()
    {
        long size = 0;
        foreach (var file in new DirectoryInfo(cache).EnumerateFiles("*.jpg").OrderByDescending(file => file.LastWriteTimeUtc))
        {
            size += file.Length;
            if (size > 67_108_864)
            {
                file.Delete();
            }
        }
    }

    internal void Tick()
    {
        var ready = pending.FirstOrDefault(item => item.Value.IsCompleted);
        if (ready.Key == null)
        {
            return;
        }
        pending.Remove(ready.Key);
        Texture2D? texture = null;
        try
        {
            var bytes = ready.Value.GetAwaiter().GetResult();
            if (bytes != null)
            {
                texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!texture.LoadImage(bytes, true))
                {
                    UnityEngine.Object.Destroy(texture);
                    texture = null;
                }
                else if (texture.width > 512 || texture.height > 512)
                {
                    UnityEngine.Object.Destroy(texture);
                    texture = null;
                }
            }
        }
        catch (Exception)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }
        }
        textures[ready.Key] = texture;
    }

    internal void Reset()
    {
        cancellation.Cancel();
        var previous = cancellation;
        _ = Task.WhenAll(pending.Values).ContinueWith(done =>
        {
            _ = done.Exception;
            previous.Dispose();
        }, TaskScheduler.Default);
        cancellation = new CancellationTokenSource();
        pending.Clear();
        foreach (var texture in textures.Values)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
        }
        textures.Clear();
    }

    public void Dispose()
    {
        Reset();
        cancellation.Dispose();
        client.Dispose();
    }
}
