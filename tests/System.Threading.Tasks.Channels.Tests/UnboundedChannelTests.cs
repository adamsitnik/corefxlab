// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace System.IO.Channels.Tests
{
    public abstract class UnboundedChannelTests : ChannelTestBase
    {
        protected abstract bool AllowSynchronousContinuations { get; }
        protected override Channel<int> CreateChannel() => Channel.CreateUnbounded<int>(
            new ChannelOptimizations
            {
                SingleReader = RequiresSingleReader,
                AllowSynchronousContinuations = AllowSynchronousContinuations
            });
        protected override Channel<int> CreateFullChannel() => null;

        [Fact]
        public async Task Complete_BeforeEmpty_NoWaiters_TriggersCompletion()
        {
            Channel<int> c = CreateChannel();
            Assert.True(c.Out.TryWrite(42));
            c.Out.Complete();
            Assert.False(c.In.Completion.IsCompleted);
            Assert.Equal(42, await c.In.ReadAsync());
            await c.In.Completion;
        }

        [Fact]
        public void TryWrite_TryRead_Many()
        {
            Channel<int> c = CreateChannel();

            const int NumItems = 100000;
            for (int i = 0; i < NumItems; i++)
            {
                Assert.True(c.Out.TryWrite(i));
            }
            for (int i = 0; i < NumItems; i++)
            {
                Assert.True(c.In.TryRead(out int result));
                Assert.Equal(i, result);
            }
        }

        [Fact]
        public void TryWrite_TryRead_OneAtATime()
        {
            Channel<int> c = CreateChannel();

            for (int i = 0; i < 10; i++)
            {
                Assert.True(c.Out.TryWrite(i));
                Assert.True(c.In.TryRead(out int result));
                Assert.Equal(i, result);
            }
        }

        [Fact]
        public void WaitForReadAsync_DataAvailable_CompletesSynchronously()
        {
            Channel<int> c = CreateChannel();
            Assert.True(c.Out.TryWrite(42));
            AssertSynchronousTrue(c.In.WaitToReadAsync());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task WriteMany_ThenComplete_SuccessfullyReadAll(int readMode)
        {
            Channel<int> c = CreateChannel();
            for (int i = 0; i < 10; i++)
            {
                Assert.True(c.Out.TryWrite(i));
            }

            c.Out.Complete();
            Assert.False(c.In.Completion.IsCompleted);

            for (int i = 0; i < 10; i++)
            {
                Assert.False(c.In.Completion.IsCompleted);
                switch (readMode)
                {
                    case 0:
                        int result;
                        Assert.True(c.In.TryRead(out result));
                        Assert.Equal(i, result);
                        break;
                    case 1:
                        Assert.Equal(i, await c.In.ReadAsync());
                        break;
                }
            }

            await c.In.Completion;
        }

        [Fact]
        public void AllowSynchronousContinuations_ReadAsync_ContinuationsInvokedAccordingToSetting()
        {
            Channel<int> c = CreateChannel();

            int expectedId = Environment.CurrentManagedThreadId;
            Task r = c.In.ReadAsync().AsTask().ContinueWith(_ =>
            {
                Assert.Equal(AllowSynchronousContinuations, expectedId == Environment.CurrentManagedThreadId);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            Assert.Equal(TaskStatus.RanToCompletion, c.Out.WriteAsync(42).Status);
            ((IAsyncResult)r).AsyncWaitHandle.WaitOne(); // avoid inlining the continuation
            r.GetAwaiter().GetResult();
        }

        [Fact]
        public void AllowSynchronousContinuations_CompletionTask_ContinuationsInvokedAccordingToSetting()
        {
            Channel<int> c = CreateChannel();

            int expectedId = Environment.CurrentManagedThreadId;
            Task r = c.In.Completion.ContinueWith(_ =>
            {
                Assert.Equal(AllowSynchronousContinuations, expectedId == Environment.CurrentManagedThreadId);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            Assert.True(c.Out.TryComplete());
            ((IAsyncResult)r).AsyncWaitHandle.WaitOne(); // avoid inlining the continuation
            r.GetAwaiter().GetResult();
        }
    }

    public abstract class SingleReaderUnboundedChannelTests : UnboundedChannelTests
    {
        protected override bool RequiresSingleReader => true;

        [Fact]
        public void ValidateInternalDebuggerAttributes()
        {
            Channel<int> c = CreateChannel();
            Assert.True(c.Out.TryWrite(1));
            Assert.True(c.Out.TryWrite(2));

            object queue = DebuggerAttributes.GetFieldValue(c, "_items");
            DebuggerAttributes.ValidateDebuggerDisplayReferences(queue);
            DebuggerAttributes.ValidateDebuggerTypeProxyProperties(queue);
        }

        [Fact]
        public async Task MultipleWaiters_CancelsPreviousWaiter()
        {
            Channel<int> c = CreateChannel();
            Task<bool> t1 = c.In.WaitToReadAsync();
            Task<bool> t2 = c.In.WaitToReadAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t1);
            Assert.True(c.Out.TryWrite(42));
            Assert.True(await t2);
        }

        [Fact]
        public void Stress_TryWrite_TryRead()
        {
            const int NumItems = 3000000;
            Channel<int> c = CreateChannel();

            Task.WaitAll(
                Task.Run(async () =>
                {
                    int received = 0;
                    while (await c.In.WaitToReadAsync())
                    {
                        while (c.In.TryRead(out int i))
                        {
                            Assert.Equal(received, i);
                            received++;
                        }
                    }
                }),
                Task.Run(() =>
                {
                    for (int i = 0; i < NumItems; i++)
                    {
                        Assert.True(c.Out.TryWrite(i));
                    }
                    c.Out.Complete();
                }));
        }
    }

    public sealed class SyncMultiReaderUnboundedChannelTests : UnboundedChannelTests
    {
        protected override bool AllowSynchronousContinuations => true;
    }

    public sealed class AsyncMultiReaderUnboundedChannelTests : UnboundedChannelTests
    {
        protected override bool AllowSynchronousContinuations => false;
    }

    public sealed class SyncSingleReaderUnboundedChannelTests : SingleReaderUnboundedChannelTests
    {
        protected override bool AllowSynchronousContinuations => true;
    }

    public sealed class AsyncSingleReaderUnboundedChannelTests : SingleReaderUnboundedChannelTests
    {
        protected override bool AllowSynchronousContinuations => false;
    }
}
