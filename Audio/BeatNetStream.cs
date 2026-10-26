using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FMOD;
using FMODUnity;

namespace BEATNET;

internal sealed class BeatNetStream : IDisposable
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    private readonly Uri url;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Dictionary<IntPtr, Read> pending = new();
    private readonly List<Task> tasks = new();
    private CREATESOUNDEXINFO info;
    private int error;
    internal Task<uint> Length { get; }
    internal RESULT Error => (RESULT)Volatile.Read(ref error);

    internal BeatNetStream(Uri url)
    {
        this.url = url;
        info = new CREATESOUNDEXINFO
        {
            cbsize = Marshal.SizeOf<CREATESOUNDEXINFO>(),
            fileuseropen = OpenFile,
            fileuserclose = CloseFile,
            fileuserasyncread = ReadFile,
            fileuserasynccancel = CancelRead,
            filebuffersize = 32768
        };
        Length = Task.Run(GetLength);
        tasks.Add(Length);
    }

    internal RESULT Open(out Sound sound) => RuntimeManager.CoreSystem.createStream("preview.mp3",
        MODE.NONBLOCKING | MODE._2D | MODE.IGNORETAGS, ref info, out sound);

    private async Task<uint> GetLength()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var length = response.Content.Headers.ContentLength;
        if (!length.HasValue || length <= 0 || length > 8 * 1024 * 1024)
        {
            throw new InvalidDataException("Invalid preview length");
        }
        return (uint)length.Value;
    }

    private RESULT OpenFile(IntPtr name, ref uint size, ref IntPtr handle, IntPtr userdata)
    {
        if (cancellation.IsCancellationRequested || !Length.IsCompletedSuccessfully)
        {
            return RESULT.ERR_FILE_BAD;
        }
        size = Length.GetAwaiter().GetResult();
        handle = new IntPtr(1);
        return RESULT.OK;
    }

    private RESULT CloseFile(IntPtr handle, IntPtr userdata) => RESULT.OK;

    private RESULT ReadFile(IntPtr pointer, IntPtr userdata)
    {
        if (cancellation.IsCancellationRequested)
        {
            return RESULT.ERR_FILE_DISKEJECTED;
        }
        var read = new Read(pointer);
        lock (pending)
        {
            if (pending.ContainsKey(pointer))
            {
                return RESULT.ERR_FILE_BAD;
            }
            pending.Add(pointer, read);
        }
        lock (tasks)
        {
            tasks.Add(Task.Run(() => Fetch(read)));
        }
        return RESULT.OK;
    }

    private RESULT CancelRead(IntPtr pointer, IntPtr userdata)
    {
        lock (pending)
        {
            if (pending.TryGetValue(pointer, out var read))
            {
                Complete(read, null, RESULT.ERR_FILE_DISKEJECTED);
            }
        }
        return RESULT.OK;
    }

    private async Task Fetch(Read read)
    {
        try
        {
            var length = Length.GetAwaiter().GetResult();
            if (read.Offset >= length)
            {
                Complete(read, Array.Empty<byte>(), RESULT.ERR_FILE_EOF);
                return;
            }
            var count = (int)Math.Min(read.Size, length - read.Offset);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(read.Offset, read.Offset + count - 1);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            using var registration = timeout.Token.Register(response.Dispose);
            var range = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent || range?.From != read.Offset
                || range.To != read.Offset + count - 1 || range.Length != length)
            {
                throw new InvalidDataException("Invalid preview range");
            }
            using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var bytes = new byte[count];
            var received = 0;
            while (received < count)
            {
                var size = await input.ReadAsync(bytes, received, count - received, timeout.Token).ConfigureAwait(false);
                if (size == 0)
                {
                    throw new EndOfStreamException();
                }
                received += size;
            }
            Complete(read, bytes, count < read.Size ? RESULT.ERR_FILE_EOF : RESULT.OK);
        }
        catch (Exception)
        {
            Complete(read, null, RESULT.ERR_FILE_BAD);
        }
    }

    private void Complete(Read read, byte[]? bytes, RESULT result)
    {
        lock (pending)
        {
            if (!pending.TryGetValue(read.Pointer, out var current) || current != read)
            {
                return;
            }
            pending.Remove(read.Pointer);
            if (result == RESULT.ERR_FILE_BAD && !cancellation.IsCancellationRequested)
            {
                Interlocked.CompareExchange(ref error, (int)RESULT.ERR_HTTP, 0);
            }
            read.Complete(bytes, result);
        }
    }

    internal void Cancel() => cancellation.Cancel();

    public void Dispose()
    {
        Cancel();
        Task completed;
        lock (tasks)
        {
            completed = Task.WhenAll(tasks);
        }
        _ = completed.ContinueWith(task =>
        {
            _ = task.Exception;
            cancellation.Dispose();
        }, TaskScheduler.Default);
    }

    private sealed class Read
    {
        private static readonly int BytesReadOffset = Marshal.OffsetOf<ASYNCREADINFO>(nameof(ASYNCREADINFO.bytesread)).ToInt32();
        private readonly ASYNCREADINFO info;
        internal IntPtr Pointer { get; }
        internal uint Offset => info.offset;
        internal uint Size => info.sizebytes;

        internal Read(IntPtr pointer)
        {
            Pointer = pointer;
            info = Marshal.PtrToStructure<ASYNCREADINFO>(pointer);
        }

        internal void Complete(byte[]? bytes, RESULT result)
        {
            var count = bytes?.Length ?? 0;
            if (count > 0)
            {
                Marshal.Copy(bytes!, 0, info.buffer, count);
            }
            Marshal.WriteInt32(Pointer, BytesReadOffset, count);
            info.done(Pointer, result);
        }
    }
}
