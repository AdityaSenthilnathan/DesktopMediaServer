using DesktopMediaServer.Media;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System;
using System.Net;

namespace DesktopMediaServer.Server
{
    public sealed class NowPlayingServer : IDisposable
    {
        private WebApplication? _app;
        public bool IsRunning => _app != null;

        public async Task StartAsync(GsmtcController media, int port, string token)
        {
            if (_app != null) return;

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, port);
                options.Listen(IPAddress.Any, port);
            });

            var app = builder.Build();

            app.Use(async (ctx, next) =>
            {
                var auth = ctx.Request.Headers.Authorization.ToString();
                if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer "))
                {
                    ctx.Response.StatusCode = 401;
                    await ctx.Response.WriteAsync("Missing Bearer token");
                    return;
                }

                var supplied = auth.Substring("Bearer ".Length);
                if (!string.Equals(supplied, token, StringComparison.Ordinal))
                {
                    ctx.Response.StatusCode = 403;
                    await ctx.Response.WriteAsync("Invalid token");
                    return;
                }

                await next();
            });

            app.MapGet("/now-playing", async (bool? art) =>
            {
                return await media.GetNowPlayingAsync(art == true);
            });

            app.MapPost("/play-pause", async () => new { ok = await media.PlayPauseAsync() });
            app.MapPost("/play", async () => new { ok = await media.PlayAsync() });
            app.MapPost("/pause", async () => new { ok = await media.PauseAsync() });
            app.MapPost("/next", async () => new { ok = await media.NextAsync() });
            app.MapPost("/previous", async () => new { ok = await media.PreviousAsync() });

            _app = app;
            await _app.StartAsync();
        }

        public async Task StopAsync()
        {
            if (_app == null) return;
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }

        public void Dispose() => _ = StopAsync();
    }
}