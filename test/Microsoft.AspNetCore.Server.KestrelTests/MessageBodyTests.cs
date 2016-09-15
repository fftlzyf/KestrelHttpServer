// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    /// <summary>
    /// Summary description for MessageBodyTests
    /// </summary>
    public class MessageBodyTests
    {
        [Fact]
        public void Http10ConnectionClose()
        {
            using (var input = new TestInput())
            {
                var body = MessageBody.For(HttpVersion.Http10, new FrameRequestHeaders(), input.FrameContext);
                var stream = new FrameRequestStream();
                stream.StartAcceptingReads(body);

                input.Add("Hello", true);

                var buffer1 = new byte[1024];
                var count1 = stream.Read(buffer1, 0, 1024);
                AssertASCII("Hello", new ArraySegment<byte>(buffer1, 0, 5));

                var buffer2 = new byte[1024];
                var count2 = stream.Read(buffer2, 0, 1024);
                Assert.Equal(0, count2);
            }
        }

        [Fact]
        public async Task Http10ConnectionCloseAsync()
        {
            using (var input = new TestInput())
            {
                var body = MessageBody.For(HttpVersion.Http10, new FrameRequestHeaders(), input.FrameContext);
                var stream = new FrameRequestStream();
                stream.StartAcceptingReads(body);

                input.Add("Hello", true);

                var buffer1 = new byte[1024];
                var count1 = await stream.ReadAsync(buffer1, 0, 1024);
                AssertASCII("Hello", new ArraySegment<byte>(buffer1, 0, 5));

                var buffer2 = new byte[1024];
                var count2 = await stream.ReadAsync(buffer2, 0, 1024);
                Assert.Equal(0, count2);
            }
        }

        [Fact]
        public async Task CanHandleLargeBlocks()
        {
            using (var input = new TestInput())
            {
                var body = MessageBody.For(HttpVersion.Http10, new FrameRequestHeaders(), input.FrameContext);
                var stream = new FrameRequestStream();
                stream.StartAcceptingReads(body);

                // Input needs to be greater than 4032 bytes to allocate a block not backed by a slab.
                var largeInput = new string('a', 8192);

                input.Add(largeInput);
                // Add a smaller block to the end so that SocketInput attempts to return the large
                // block to the memory pool.
                input.Add("Hello", fin: true);

                var ms = new MemoryStream();

                await stream.CopyToAsync(ms);
                var requestArray = ms.ToArray();
                Assert.Equal(8197, requestArray.Length);
                AssertASCII(largeInput + "Hello", new ArraySegment<byte>(requestArray, 0, requestArray.Length));

                var readBuffer = new byte[1];
                var count = await stream.ReadAsync(readBuffer, 0, 1);
                Assert.Equal(0, count);
            }
        }

        public static TheoryData<Stream> WriteStreamData => new TheoryData<Stream>
        {
            new ThrowOnWriteSynchronousStream(),
            new ThrowOnWriteAsynchronousStream()

        public static TheoryData<Stream, FrameRequestHeaders, string[]> StreamData => new TheoryData<Stream, FrameRequestHeaders, string[]>
        {
            // Remaining Data
            { new ThrowOnWriteSynchronousStream(), new FrameRequestHeaders { HeaderConnection = "close" }, new[] { "Hello ", "World!" } },
            { new ThrowOnWriteAsynchronousStream(), new FrameRequestHeaders { HeaderConnection = "close" }, new[] { "Hello ", "World!" } },
            // Content-Length
            { new ThrowOnWriteSynchronousStream(), new FrameRequestHeaders { HeaderContentLength = "12" }, new[] { "Hello ", "World!" } },
            { new ThrowOnWriteAsynchronousStream(), new FrameRequestHeaders { HeaderContentLength = "12" }, new[] { "Hello ", "World!" } },
            // Chunked 
            { new ThrowOnWriteSynchronousStream(), new FrameRequestHeaders { HeaderTransferEncoding = "chunked" }, new[] { "6\r\nHello \r\n", "6\r\nWorld!\r\n0\r\n\r\n" } },
            { new ThrowOnWriteAsynchronousStream(), new FrameRequestHeaders { HeaderTransferEncoding = "chunked" }, new[] { "6\r\nHello \r\n", "6\r\nWorld!\r\n0\r\n\r\n" } },
        };

        [Theory]
        [MemberData(nameof(StreamData))]
        public async Task CopyToAsyncAdvancesRequestStreamWhenDestinationWriteAsyncThrows(Stream writeStream, FrameRequestHeaders headers, string[] data)
        {
            using (var input = new TestInput())
            {
                var body = MessageBody.For(HttpVersion.Http11, headers, input.FrameContext);

                input.Add(data[0]);

                await Assert.ThrowsAsync<NotImplementedException>(() => body.CopyToAsync(writeStream));

                input.Add(data[1], fin: headers.HeaderConnection == "close");

                // "Hello " should have been consumed
                var readBuffer = new byte[6];
                var count = await body.ReadAsync(new ArraySegment<byte>(readBuffer, 0, readBuffer.Length));
                Assert.Equal(6, count);
                AssertASCII("World!", new ArraySegment<byte>(readBuffer, 0, 6));

                count = await body.ReadAsync(new ArraySegment<byte>(readBuffer, 0, readBuffer.Length));
                Assert.Equal(0, count);
            }
        }

        private void AssertASCII(string expected, ArraySegment<byte> actual)
        {
            var encoding = Encoding.ASCII;
            var bytes = encoding.GetBytes(expected);
            Assert.Equal(bytes.Length, actual.Count);
            for (var index = 0; index < bytes.Length; index++)
            {
                Assert.Equal(bytes[index], actual.Array[actual.Offset + index]);
            }
        }

        private class ThrowOnWriteSynchronousStream : Stream
        {
            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override bool CanRead { get; }
            public override bool CanSeek { get; }
            public override bool CanWrite => true;
            public override long Length { get; }
            public override long Position { get; set; }
        }

        private class ThrowOnWriteAsynchronousStream : Stream
        {
            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                // The test looks for a NotImplementedException.
                // Ensure that WriteAsync is being called by not throwing that type.
                throw new Exception();
            }

            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                await Task.Delay(1);
                throw new NotImplementedException();
            }

            public override bool CanRead { get; }
            public override bool CanSeek { get; }
            public override bool CanWrite => true;
            public override long Length { get; }
            public override long Position { get; set; }
        }
    }
}