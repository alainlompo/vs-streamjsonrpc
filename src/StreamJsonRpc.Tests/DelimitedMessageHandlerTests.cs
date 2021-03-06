﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio.Threading;
using StreamJsonRpc;
using Xunit;
using Xunit.Abstractions;

public class DelimitedMessageHandlerTests : TestBase
{
    private readonly MemoryStream sendingStream = new MemoryStream();
    private readonly MemoryStream receivingStream = new MemoryStream();
    private DirectMessageHandler handler;

    public DelimitedMessageHandlerTests(ITestOutputHelper logger) : base(logger)
    {
        this.handler = new DirectMessageHandler(this.sendingStream, this.receivingStream, Encoding.UTF8);
    }

    [Fact]
    public void CanReadAndWrite()
    {
        Assert.True(handler.CanRead);
        Assert.True(handler.CanWrite);
    }

    [Fact]
    public async Task Ctor_AcceptsNullSendingStream()
    {
        var handler = new DirectMessageHandler(null, this.receivingStream, Encoding.UTF8);
        Assert.True(handler.CanRead);
        Assert.False(handler.CanWrite);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.WriteAsync("hi", TimeoutToken));
        string expected = "bye";
        handler.MessagesToRead.Enqueue(expected);
        string actual = await handler.ReadAsync(TimeoutToken);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Ctor_AcceptsNullReceivingStream()
    {
        var handler = new DirectMessageHandler(this.sendingStream, null, Encoding.UTF8);
        Assert.False(handler.CanRead);
        Assert.True(handler.CanWrite);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ReadAsync(TimeoutToken));
        string expected = "bye";
        await handler.WriteAsync(expected, TimeoutToken);
        string actual = await handler.WrittenMessages.DequeueAsync(TimeoutToken);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsDisposed()
    {
        IDisposableObservable observable = this.handler;
        Assert.False(observable.IsDisposed);
        this.handler.Dispose();
        Assert.True(observable.IsDisposed);
    }

    [Fact]
    public void WriteAsync_ThrowsObjectDisposedException()
    {
        this.handler.Dispose();
        Task result = this.handler.WriteAsync("content", TimeoutToken);
        Assert.Throws<ObjectDisposedException>(() => result.GetAwaiter().GetResult());
    }

    /// <summary>
    /// Verifies that when both <see cref="ObjectDisposedException"/> and <see cref="OperationCanceledException"/> are appropriate
    /// when we first invoke the method, the <see cref="OperationCanceledException"/> is thrown.
    /// </summary>
    [Fact]
    public void WriteAsync_PreferOperationCanceledException_AtEntry()
    {
        this.handler.Dispose();
        Assert.Throws<OperationCanceledException>(() => this.handler.WriteAsync("content", PrecanceledToken).GetAwaiter().GetResult());
    }

    /// <summary>
    /// Verifies that <see cref="DelimitedMessageHandler.ReadAsync(CancellationToken)"/> prefers throwing
    /// <see cref="OperationCanceledException"/> over <see cref="ObjectDisposedException"/> when both conditions
    /// apply while reading (at least when cancellation occurs first).
    /// </summary>
    [Fact]
    public async Task WriteAsync_PreferOperationCanceledException_MidExecution()
    {
        var handler = new DelayedWriter(this.sendingStream, this.receivingStream, Encoding.UTF8);

        var cts = new CancellationTokenSource();
        var writeTask = handler.WriteAsync("content", cts.Token);

        cts.Cancel();
        handler.Dispose();

        // Unblock writer. It should not throw anything as it is to emulate not recognizing the
        // CancellationToken before completing its work.
        handler.WriteBlock.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writeTask);
    }

    [Fact]
    public void ReadAsync_ThrowsObjectDisposedException()
    {
        this.handler.Dispose();
        Task result = this.handler.ReadAsync(TimeoutToken);
        Assert.Throws<ObjectDisposedException>(() => result.GetAwaiter().GetResult());
        Assert.Throws<OperationCanceledException>(() => this.handler.ReadAsync(PrecanceledToken).GetAwaiter().GetResult());
    }

    /// <summary>
    /// Verifies that when both <see cref="ObjectDisposedException"/> and <see cref="OperationCanceledException"/> are appropriate
    /// when we first invoke the method, the <see cref="OperationCanceledException"/> is thrown.
    /// </summary>
    [Fact]
    public void ReadAsync_PreferOperationCanceledException_AtEntry()
    {
        this.handler.Dispose();
        Assert.Throws<OperationCanceledException>(() => this.handler.ReadAsync(PrecanceledToken).GetAwaiter().GetResult());
    }

    /// <summary>
    /// Verifies that <see cref="DelimitedMessageHandler.ReadAsync(CancellationToken)"/> prefers throwing
    /// <see cref="OperationCanceledException"/> over <see cref="ObjectDisposedException"/> when both conditions
    /// apply while reading (at least when cancellation occurs first).
    /// </summary>
    [Fact]
    public async Task ReadAsync_PreferOperationCanceledException_MidExecution()
    {
        var cts = new CancellationTokenSource();
        var readTask = this.handler.ReadAsync(cts.Token);

        cts.Cancel();
        this.handler.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);
    }

    private class DelayedWriter : DelimitedMessageHandler
    {
        internal readonly AsyncManualResetEvent WriteBlock = new AsyncManualResetEvent();

        public DelayedWriter(Stream sendingStream, Stream receivingStream, Encoding encoding)
            : base(sendingStream, receivingStream, encoding)
        {
        }

        protected override Task<string> ReadCoreAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        protected override Task WriteCoreAsync(string content, Encoding contentEncoding, CancellationToken cancellationToken)
        {
            return WriteBlock.WaitAsync();
        }
    }
}
