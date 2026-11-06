using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Downloads;

public sealed class AdaptiveDownloader(HttpClient http)
{
    public static int WorkerCount => Math.Min(Environment.ProcessorCount, 12) * 2;
    private const int BufferSize = 64 * 1024;
    private const int MinSegmentSize = 512 * 1024;

    public Task DownloadAsync(DownloadFile file, IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        DownloadAsync([file], progress, cancellationToken: cancellationToken);

    public async Task DownloadAsync(IReadOnlyList<DownloadFile> files, IProgress<DownloadProgress>? progress = null,
        Func<DownloadFile, CancellationToken, Task>? fileCompleted = null,
        CancellationToken cancellationToken = default)
    {
        using var batch = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var gate = new object();
        var changed = NewSignal();
        // Start unknown-size and larger files early to reduce the download tail.
        var transfers = files.OrderByDescending(file => file.Size ?? long.MaxValue)
            .Select(file => new Transfer(file, batch.Token)).ToArray();
        var running = new Dictionary<Task, (Transfer File, Segment? Segment)>();
        var candidates = transfers;
        var lastReport = Stopwatch.GetTimestamp();
        long transferredBytes = 0;

        bool CanSchedule(Transfer file) => !file.Done && !file.Finishing && file.Error is null &&
            (!file.Started || (file.CanSplit && (file.Pending.Count > 0 ||
                file.Segments.Any(part => !part.Yielded && part.End - part.Next >= MinSegmentSize * 2))));

        void Report(bool force = false)
        {
            // Called under gate: progress observers see ordered snapshots from all workers.
            if (!force && Stopwatch.GetElapsedTime(lastReport).TotalMilliseconds < 100) return;
            lastReport = Stopwatch.GetTimestamp();
            var total = transfers.All(file => file.Length.HasValue) ? transfers.Sum(file => file.Length!.Value) : (long?)null;
            progress?.Report(new DownloadProgress(transfers.Sum(file => file.Downloaded), total, transferredBytes));
        }

        void Signal()
        {
            var previous = changed;
            changed = NewSignal();
            previous.TrySetResult();
        }

        async Task ReceiveAsync(Transfer file, Segment segment)
        {
            var ct = file.Cancellation.Token;
            if (segment.Initial)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file.Request.Path))!);
                file.Handle = File.OpenHandle(file.TemporaryPath, FileMode.Create, FileAccess.Write,
                    FileShare.None, FileOptions.Asynchronous | FileOptions.RandomAccess);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(http.Timeout);
            var useFallback = !segment.Initial && file.UseRangeFallback;
            using var request = new HttpRequestMessage(HttpMethod.Get, useFallback ? file.Request.RangeFallbackUrl : file.Url);
            if (useFallback) request.Options.Set(DownloadFile.BypassMirror, true);
            request.Headers.AcceptEncoding.ParseAdd("identity");
            if (!file.Sequential)
            {
                request.Headers.Range = new RangeHeaderValue(segment.Start, segment.RequestEnd - 1);
                if (!segment.Initial && file.Validator is not null)
                    request.Headers.IfRange = file.Validator;
            }

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!file.Sequential && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                throw new RangeUnavailableException();
            response.EnsureSuccessStatusCode();
            var range = response.Content.Headers.ContentRange;
            var partial = response.StatusCode == HttpStatusCode.PartialContent;
            if (!segment.Initial && !partial) throw new RangeUnavailableException();
            if (partial && (range?.Unit != "bytes" || range.From != segment.Start ||
                range.Length is not > 0 || range.To != (segment.RequestEnd ?? range.Length) - 1 ||
                (response.Content.Headers.ContentLength is { } contentLength && contentLength != range.To - range.From + 1)))
                throw new InvalidDataException($"Invalid Content-Range from {file.Url}.");
            if (response.Content.Headers.ContentEncoding.Any(encoding => !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Unexpected content encoding from {file.Url}.");

            lock (gate)
            {
                var length = partial ? range!.Length : response.Content.Headers.ContentLength;
                if (length is not null && file.Length is not null && length != file.Length)
                    throw new InvalidDataException($"Download size changed for {file.Request.Path}.");
                if (segment.Initial)
                {
                    file.Length = length ?? file.Length;
                    segment.End = file.Length ?? long.MaxValue;
                    file.CanSplit = partial && !file.Sequential;
                    file.UseRangeFallback = partial && file.Request.RangeFallbackUrl is not null &&
                        request.Options.TryGetValue(DownloadFile.BypassMirror, out var usedOfficial) && usedOfficial;
                    var etag = response.Headers.ETag;
                    file.Validator = etag is { IsWeak: false } ? new RangeConditionHeaderValue(etag)
                        : response.Content.Headers.LastModified is { } modified ? new RangeConditionHeaderValue(modified) : null;
                    Signal();
                }
            }

            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var buffer = new byte[BufferSize];
            while (true)
            {
                long offset;
                int count;
                lock (gate)
                {
                    offset = segment.Next;
                    count = (int)Math.Min(buffer.Length, segment.End - offset);
                    if (count > 0 && file.CanSplit && file.Segments.Count > 1 &&
                        candidates.Any(other => other != file && CanSchedule(other) &&
                            file.Segments.Count(part => !part.Yielded) > other.Segments.Count(part => !part.Yielded) + 1))
                    {
                        segment.Yielded = true;
                        return;
                    }
                    // Reserve this buffer before releasing gate so splitting never overlaps an in-flight write.
                    segment.Next += count;
                }
                if (count == 0) break;
                while (count > 0)
                {
                    timeout.CancelAfter(http.Timeout);
                    var read = await input.ReadAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        if (file.Length is not null) throw new EndOfStreamException($"Download ended early for {file.Request.Path}.");
                        break;
                    }
                    await RandomAccess.WriteAsync(file.Handle!, buffer.AsMemory(0, read), offset, timeout.Token).ConfigureAwait(false);
                    offset += read;
                    count -= read;
                    lock (gate)
                    {
                        file.Downloaded += read;
                        transferredBytes += read;
                        Report();
                    }
                }
                if (count > 0) break;
            }
            lock (gate)
            {
                if (file.Length is null) file.Length = file.Downloaded;
            }
        }

        async Task FinishAsync(Transfer file)
        {
            file.Handle!.Dispose();
            file.Handle = null;
            var ct = file.Cancellation.Token;
            if (file.Request.Hash is { } expected)
            {
                await using var input = File.OpenRead(file.TemporaryPath);
                var digest = await CryptographicOperations.HashDataAsync(
                    file.Request.HashAlgorithm ?? HashAlgorithmName.SHA1, input, ct).ConfigureAwait(false);
                if (!string.Equals(Convert.ToHexString(digest), expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Checksum mismatch for {file.Request.Path}.");
            }
            ct.ThrowIfCancellationRequested();
            File.Move(file.TemporaryPath, file.Request.Path, overwrite: true);
            if (fileCompleted is not null) await fileCompleted(file.Request, ct).ConfigureAwait(false);
        }

        try
        {
            lock (gate) Report(force: true);
            while (transfers.Any(file => !file.Done))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var task in running.Keys.Where(task => task.IsCompleted).ToArray())
                {
                    var (file, segment) = running[task];
                    running.Remove(task);
                    try
                    {
                        await task.ConfigureAwait(false);
                        if (segment is null)
                        {
                            lock (gate) file.Done = true;
                        }
                    }
                    catch (Exception error)
                    {
                        if (file.Error is null)
                        {
                            lock (gate) file.Error = error;
                            await file.Cancellation.CancelAsync().ConfigureAwait(false);
                        }
                    }
                    lock (gate)
                    {
                        if (segment is not null)
                        {
                            file.Segments.Remove(segment);
                            if (segment.Yielded) file.Pending.Add(new Segment(segment.Next, segment.End, initial: false));
                        }
                    }
                    if (file.Done)
                    {
                        lock (gate) Report(force: true);
                        continue;
                    }
                    if (file.Segments.Count != 0) continue;
                    if (file.Error is { } failure)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (failure is RangeUnavailableException && !file.Sequential)
                            file.Sequential = true;
                        else if (file.UseRangeFallback && failure is (HttpRequestException or InvalidDataException or EndOfStreamException or OperationCanceledException))
                            file.Sequential = true;
                        else if ((failure is HttpRequestException or InvalidDataException or EndOfStreamException or OperationCanceledException) &&
                                 file.UrlIndex < (file.Request.Mirrors?.Count ?? 0))
                        {
                            file.UrlIndex++;
                            file.Sequential = false;
                        }
                        else
                            ExceptionDispatchInfo.Capture(failure).Throw();
                        lock (gate)
                        {
                            file.Reset(batch.Token);
                            Report(force: true);
                        }
                    }
                    else if (file.Pending.Count == 0)
                    {
                        lock (gate) file.Finishing = true;
                        running.Add(Task.Run(() => FinishAsync(file)), (file, null));
                    }
                }

                Task notification;
                lock (gate)
                {
                    candidates = transfers.Where(file => !file.Done && !file.Finishing && file.Error is null).ToArray();
                    while (running.Count < WorkerCount)
                    {
                        var file = transfers.FirstOrDefault(file => !file.Started);
                        Segment segment;
                        if (file is not null)
                        {
                            file.Started = true;
                            segment = new Segment(0, null, initial: true);
                        }
                        else
                        {
                            file = candidates.Where(CanSchedule)
                                .OrderBy(file => file.Segments.Count).FirstOrDefault();
                            if (file is null) break;
                            if (file.Pending.Count > 0)
                            {
                                segment = file.Pending.MaxBy(part => part.End - part.Next)!;
                                file.Pending.Remove(segment);
                            }
                            else
                            {
                                var longest = file.Segments.Where(part => !part.Yielded).MaxBy(part => part.End - part.Next)!;
                                var split = longest.Next + (longest.End - longest.Next) / 2;
                                segment = new Segment(split, longest.End, initial: false);
                                longest.End = split;
                            }
                        }
                        file.Segments.Add(segment);
                        running.Add(Task.Run(() => ReceiveAsync(file, segment)), (file, segment));
                    }
                    notification = changed.Task;
                }
                if (running.Count > 0)
                    await Task.WhenAny(running.Keys.Append(notification)).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await batch.CancelAsync().ConfigureAwait(false);
            try { await Task.WhenAll(running.Keys).ConfigureAwait(false); }
            catch { /* Propagate the original failure after all writers have stopped. */ }
            foreach (var file in transfers) file.Dispose();
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RangeUnavailableException : Exception;

    private sealed class Segment(long start, long? end, bool initial)
    {
        public long Start { get; } = start;
        public long? RequestEnd { get; } = end;
        public bool Initial { get; } = initial;
        public bool Yielded;
        public long Next = start;
        public long End = end ?? long.MaxValue;
    }

    private sealed class Transfer(DownloadFile request, CancellationToken token) : IDisposable
    {
        public DownloadFile Request { get; } = request;
        public string TemporaryPath { get; } = request.Path + "." + Guid.NewGuid().ToString("N") + ".part";
        public string Url => UrlIndex == 0 ? Request.Url : Request.Mirrors![UrlIndex - 1];
        public int UrlIndex;
        public bool Sequential;
        public bool Started;
        public bool Finishing;
        public bool Done;
        public bool CanSplit;
        public bool UseRangeFallback;
        public long? Length = request.Size;
        public long Downloaded;
        public SafeFileHandle? Handle;
        public RangeConditionHeaderValue? Validator;
        public Exception? Error;
        public CancellationTokenSource Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        public List<Segment> Segments { get; } = [];
        public List<Segment> Pending { get; } = [];

        public void Reset(CancellationToken token)
        {
            Handle?.Dispose();
            Handle = null;
            File.Delete(TemporaryPath);
            Cancellation.Dispose();
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            Started = Finishing = CanSplit = UseRangeFallback = false;
            Error = null;
            Length = Request.Size;
            Downloaded = 0;
            Validator = null;
            Pending.Clear();
        }

        public void Dispose()
        {
            Handle?.Dispose();
            Cancellation.Dispose();
            if (File.Exists(TemporaryPath)) File.Delete(TemporaryPath);
        }
    }
}
