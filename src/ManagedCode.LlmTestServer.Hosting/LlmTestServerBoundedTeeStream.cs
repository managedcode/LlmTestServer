namespace ManagedCode.LlmTestServer.Hosting;

internal sealed class LlmTestServerBoundedWriteTeeStream(Stream inner, int maxCaptureBytes) : Stream
{
    private readonly LlmTestServerBoundedCaptureBuffer _capture = new(maxCaptureBytes);
    private int _successfulFlushCount;

    public long BytesObserved => _capture.BytesObserved;

    public int SuccessfulFlushCount => Volatile.Read(ref _successfulFlushCount);

    public bool IsTruncated => _capture.IsTruncated;

    public bool CaptureFailed => _capture.CaptureFailed;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public byte[] GetCapturedBytes()
    {
        return _capture.GetCapturedBytes();
    }

    public override void Flush()
    {
        inner.Flush();
        Interlocked.Increment(ref _successfulFlushCount);
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await inner.FlushAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _successfulFlushCount);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        inner.Write(buffer, offset, count);
        _capture.TryCapture(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        inner.Write(buffer);
        _capture.TryCapture(buffer);
    }

    public override void WriteByte(byte value)
    {
        inner.WriteByte(value);
        Span<byte> buffer = [value];
        _capture.TryCapture(buffer);
    }

    public override async Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        _capture.TryCapture(buffer.AsSpan(offset, count));
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        _capture.TryCapture(buffer.Span);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _capture.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class LlmTestServerBoundedReadTeeStream(Stream inner, int maxCaptureBytes) : Stream
{
    private readonly LlmTestServerBoundedCaptureBuffer _capture = new(maxCaptureBytes);

    public long BytesObserved => _capture.BytesObserved;

    public bool IsTruncated => _capture.IsTruncated;

    public bool CaptureFailed => _capture.CaptureFailed;

    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanTimeout => inner.CanTimeout;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override int ReadTimeout
    {
        get => inner.ReadTimeout;
        set => inner.ReadTimeout = value;
    }

    public override int WriteTimeout
    {
        get => inner.WriteTimeout;
        set => inner.WriteTimeout = value;
    }

    public byte[] GetCapturedBytes()
    {
        return _capture.GetCapturedBytes();
    }

    public override void Flush()
    {
        inner.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return inner.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        _capture.TryCapture(buffer.AsSpan(offset, read));
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        _capture.TryCapture(buffer[..read]);
        return read;
    }

    public override int ReadByte()
    {
        var value = inner.ReadByte();
        if (value >= 0)
        {
            Span<byte> buffer = [(byte)value];
            _capture.TryCapture(buffer);
        }

        return value;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        var read = await inner
            .ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
            .ConfigureAwait(false);
        _capture.TryCapture(buffer.AsSpan(offset, read));
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        _capture.TryCapture(buffer.Span[..read]);
        return read;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        inner.Write(buffer);
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        return inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        return inner.WriteAsync(buffer, cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return inner.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        inner.SetLength(value);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _capture.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class LlmTestServerBoundedCaptureBuffer(int maxCaptureBytes) : IDisposable
{
    private readonly MemoryStream _capture = new(Math.Min(maxCaptureBytes, 4096));
    private long _bytesObserved;
    private int _captureFailed;

    public long BytesObserved => Interlocked.Read(ref _bytesObserved);

    public bool CaptureFailed => Volatile.Read(ref _captureFailed) != 0;

    public bool IsTruncated
    {
        get
        {
            lock (_capture)
            {
                return BytesObserved > _capture.Length;
            }
        }
    }

    public byte[] GetCapturedBytes()
    {
        try
        {
            lock (_capture)
            {
                return _capture.ToArray();
            }
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref _captureFailed, 1);
            return [];
        }
    }

    public void TryCapture(ReadOnlySpan<byte> buffer)
    {
        Interlocked.Add(ref _bytesObserved, buffer.Length);
        if (buffer.IsEmpty || CaptureFailed)
        {
            return;
        }

        try
        {
            lock (_capture)
            {
                var remaining = maxCaptureBytes - checked((int)_capture.Length);
                if (remaining > 0)
                {
                    _capture.Write(buffer[..Math.Min(buffer.Length, remaining)]);
                }
            }
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref _captureFailed, 1);
        }
    }

    public void Dispose()
    {
        _capture.Dispose();
    }
}
