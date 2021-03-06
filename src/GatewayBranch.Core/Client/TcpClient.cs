﻿using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using GatewayBranch.Core.Codec;
using GatewayBranch.Core.Handler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace GatewayBranch.Core.Client
{
    internal class TcpClient : ITcpClient
    {
        public string Id { get; set; } = "default";

        readonly MultithreadEventLoopGroup eventLoopGroup;
        readonly Bootstrap bootstrap;
        readonly ITcpClientSessionManager sessionManager;
        readonly ILogger logger;

        public TcpClient(IServiceProvider serviceProvider, ITcpClientSessionManager sessionManager, ILogger<TcpClient> logger)
        {
            this.logger = logger;
            this.sessionManager = sessionManager;
            eventLoopGroup = new MultithreadEventLoopGroup();
            bootstrap = new Bootstrap().Group(eventLoopGroup)
                .Channel<TcpSocketChannel>()
                .Option(ChannelOption.TcpNodelay, true)
                .Option(ChannelOption.ConnectTimeout, TimeSpan.FromSeconds(30))
                .Handler(new ActionChannelInitializer<ISocketChannel>(channel =>
                {
                    var scope = serviceProvider.CreateScope().ServiceProvider;
                    IChannelPipeline pipeline = channel.Pipeline;
                    pipeline.AddLast(new IdleStateHandler(60 * 30, 60 * 30, 0));
                    pipeline.AddLast(scope.GetRequiredService<TcpMetadataDecoder>());
                    pipeline.AddLast(scope.GetRequiredService<TcpMetadataEncoder>());
                    pipeline.AddLast(scope.GetRequiredService<TcpClientHandler>());
                }));
        }

        public Task<ISession> ConnectAsync(string ip, int port, string phoneNumber) => ConnectAsync(new IPEndPoint(IPAddress.Parse(ip), port), phoneNumber);
        public async Task<ISession> ConnectAsync(EndPoint endPoint, string phoneNumber)
        {
            var channel = await bootstrap.ConnectAsync(endPoint);
            ISession session = new Session { Channel = channel, PhoneNumber = phoneNumber };
            sessionManager.Add(session);
            return session;
        }
        public Task CloseAsync(string phoneNumber)
        {
            return Task.Run(() => sessionManager.RemoveByPhoneNumber(phoneNumber));
        }
        public Task CloseBySessionIdAsync(string sessionId)
        {
            logger.LogInformation($"断开分发链路 {sessionId}");
            return Task.Run(() => sessionManager.RemoveById(sessionId));
        }

        public Task Send(string phoneNumber, byte[] data) => sessionManager.GetSession(phoneNumber).Send(data);

        public ISession GetSession(string sessionId) => sessionManager.GetSessionById(sessionId);
        public ISession GetSessionByServerSessionId(string sessionId) => sessionManager.GetSession(sessionId);

        public IEnumerable<ISession> Sesions() => sessionManager.GetSessions();
    }
    public interface ITcpClient
    {
        public string Id { get; set; }
        Task<ISession> ConnectAsync(string ip, int port, string phoneNumber = null);
        Task<ISession> ConnectAsync(EndPoint endPoint, string phoneNumber = null);
        Task CloseAsync(string phoneNumber);
        Task CloseBySessionIdAsync(string sessionId);
        ISession GetSession(string sessionId);
        ISession GetSessionByServerSessionId(string sessionId);
        IEnumerable<ISession> Sesions();
        Task Send(string phoneNumber, byte[] data);
    }
}
