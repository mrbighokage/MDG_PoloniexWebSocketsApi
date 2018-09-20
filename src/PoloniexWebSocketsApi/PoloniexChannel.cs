﻿// BSD 2-Clause License
// 
// Copyright(c) 2018, Elad Zelingher
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 
// * Redistributions of source code must retain the above copyright notice, this
// list of conditions and the following disclaimer.
// 
// * Redistributions in binary form must reproduce the above copyright notice,
// this list of conditions and the following disclaimer in the documentation
// and/or other materials provided with the distribution.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED.IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PoloniexWebSocketsApi.Logging;

namespace PoloniexWebSocketsApi
{
    // Based on this sample:
    // https://code.msdn.microsoft.com/vstudio/The-simple-WebSocket-4524921c
    // Based on WampSharp WebSocketWrapperConnection:
    // https://github.com/Code-Sharp/WampSharp/blob/wampv2/src/net45/Extensions/WampSharp.WebSockets/WebSockets/WebSocketWrapperConnection.cs
    public class PoloniexChannel : IDisposable
    {
        private static readonly ILog Logger = LogProvider.For<PoloniexChannel>();

        private readonly JsonSerializer mSerializer;
        private readonly ClientWebSocket mWebSocket;
        private CancellationTokenSource mCancellationTokenSource;
        private readonly Uri mAddressUri;
        private CancellationToken mCancellationToken;

        public PoloniexChannel(JsonSerializer jsonSerializer = null) : 
            this(new Uri("wss://api2.poloniex.com"), jsonSerializer)
        {
        }

        public PoloniexChannel(Uri addressUri, JsonSerializer jsonSerializer)
        {
            mWebSocket = new ClientWebSocket();
            mSerializer = jsonSerializer ?? new JsonSerializer();
            mSerializer.NullValueHandling = NullValueHandling.Ignore;
            mAddressUri = addressUri;
            mCancellationTokenSource = new CancellationTokenSource();
            mCancellationToken = mCancellationTokenSource.Token;
        }

        public Task SendAsync(PoloniexCommand command)
        {
            ArraySegment<byte> messageToSend = GetMessageInBytes(command);
            return mWebSocket.SendAsync(messageToSend, WebSocketMessageType, true, mCancellationToken);
        }

        private ArraySegment<byte> GetMessageInBytes(PoloniexCommand command)
        {
            StringWriter writer = new StringWriter();
            JsonTextWriter tokenWriter = new JsonTextWriter(writer){Formatting = Formatting.None};
            mSerializer.Serialize(tokenWriter, command);
            string formatted = writer.ToString();

            byte[] bytes = Encoding.UTF8.GetBytes(formatted);

            return new ArraySegment<byte>(bytes);
        }

        private WebSocketMessageType WebSocketMessageType => WebSocketMessageType.Text;

        public async Task ConnectAsync()
        {
            try
            {
                await mWebSocket.ConnectAsync(mAddressUri, mCancellationToken)
                          .ConfigureAwait(false);

                Task task = Task.Run(this.RunAsync, mCancellationToken);
            }
            catch (Exception ex)
            {
                RaiseConnectionError(ex);
                RaiseConnectionClosed();
            }
        }

        private async Task RunAsync()
        {
            try
            {
                /*We define a certain constant which will represent
                  size of received data. It is established by us and 
                  we can set any value. We know that in this case the size of the sent
                  data is very small.
                */
                const int maxMessageSize = 2048;

                // Buffer for received bits.
                ArraySegment<byte> receivedDataBuffer = new ArraySegment<byte>(new byte[maxMessageSize]);

                MemoryStream memoryStream = new MemoryStream();

                // Checks WebSocket state.
                while (IsConnected && !mCancellationToken.IsCancellationRequested)
                {
                    // Reads data.
                    WebSocketReceiveResult webSocketReceiveResult =
                        await ReadMessage(receivedDataBuffer, memoryStream).ConfigureAwait(false);

                    if (webSocketReceiveResult.MessageType != WebSocketMessageType.Close)
                    {
                        memoryStream.Position = 0;
                        OnNewMessage(memoryStream);
                    }

                    memoryStream.Position = 0;
                    memoryStream.SetLength(0);
                }
            }
            catch (Exception ex)
            {
                if (!(ex is OperationCanceledException) ||
                    !mCancellationToken.IsCancellationRequested)
                {
                    RaiseConnectionError(ex);
                }
            }

            if (mWebSocket.State != WebSocketState.CloseReceived &&
                mWebSocket.State != WebSocketState.Closed)
            {
                await CloseWebSocket().ConfigureAwait(false);
            }

            RaiseConnectionClosed();
        }

        private async Task CloseWebSocket()
        {
            try
            {
                await mWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                                            String.Empty,
                                            CancellationToken.None)
                                .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.WarnException("Failed sending a close message to client", ex);
            }
        }

        private async Task<WebSocketReceiveResult> ReadMessage(ArraySegment<byte> receivedDataBuffer, MemoryStream memoryStream)
        {
            WebSocketReceiveResult webSocketReceiveResult;

            do
            {
                webSocketReceiveResult =
                    await mWebSocket.ReceiveAsync(receivedDataBuffer, mCancellationToken)
                                    .ConfigureAwait(false);

                await memoryStream.WriteAsync(receivedDataBuffer.Array,
                                              receivedDataBuffer.Offset,
                                              webSocketReceiveResult.Count,
                                              mCancellationToken)
                                  .ConfigureAwait(false);
            }
            while (!webSocketReceiveResult.EndOfMessage);

            return webSocketReceiveResult;
        }

        private void OnNewMessage(MemoryStream payloadData)
        {
            string message = new StreamReader(payloadData, Encoding.ASCII).ReadToEnd();

            if (Logger.IsDebugEnabled())
            {
                Logger.DebugFormat("Received message: {Message}", message.ToString());
            }

            RaiseMessageArrived(message);
        }

        public void Dispose()
        {
            mCancellationTokenSource.Cancel();
            mCancellationTokenSource.Dispose();
            mCancellationTokenSource = null;
        }

        private bool IsConnected => mWebSocket.State == WebSocketState.Open;

        public event MessageArrivedDelegate MessageArrived;

        public event Action ConnectionClosed;

        public event Action<Exception> ConnectionError;

        protected virtual void RaiseMessageArrived(string message)
        {
            MessageArrived?.Invoke(message);
        }

        protected virtual void RaiseConnectionClosed()
        {
            Logger.Debug("Connection has been closed");
            ConnectionClosed?.Invoke();
        }

        protected virtual void RaiseConnectionError(Exception ex)
        {
            Logger.Error("A connection error occured", ex);
            ConnectionError?.Invoke(ex);
        }
    }
}