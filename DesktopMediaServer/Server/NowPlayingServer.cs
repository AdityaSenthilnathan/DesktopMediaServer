// DesktopMediaServer/Server/NowPlayingServer.cs
using System.IO;
using System.Text.Json;
using DesktopMediaServer.Media;
using DesktopMediaServer.Macros;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopMediaServer.Server
{
    public sealed class NowPlayingServer : IDisposable, IAsyncDisposable
    {
        private WebApplication? _app;
        public bool IsRunning => _app != null;

        private long _lastMacroTicks = 0;
        private const int MacroMinIntervalMs = 300;

        public async Task StartAsync(GsmtcController media, int port, string token, MacroRegistry macros)
        {
            if (_app != null) return;

            var builder = WebApplication.CreateBuilder();

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Any, port);
            });

            var app = builder.Build();

            // OPTIONS + basic headers
            app.Use(async (ctx, next) =>
            {
                ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
                ctx.Response.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type";
                ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";

                if (HttpMethods.IsOptions(ctx.Request.Method))
                {
                    ctx.Response.StatusCode = 204;
                    return;
                }

                await next();
            });

            // Bearer auth
            app.Use(async (ctx, next) =>
            {
                var auth = ctx.Request.Headers.Authorization.ToString();
                if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await ctx.Response.WriteAsync("Missing Bearer token");
                    return;
                }

                var supplied = auth.Substring("Bearer ".Length).Trim();
                if (!string.Equals(supplied, token, StringComparison.Ordinal))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await ctx.Response.WriteAsync("Invalid token");
                    return;
                }

                await next();
            });

            // Spotify now playing
            app.MapGet("/now-playing", async (bool? art) =>
                await media.GetNowPlayingAsync(art == true));

            app.MapPost("/play-pause", async () => Results.Ok(new { ok = await media.PlayPauseAsync() }));
            app.MapPost("/play", async () => Results.Ok(new { ok = await media.PlayAsync() }));
            app.MapPost("/pause", async () => Results.Ok(new { ok = await media.PauseAsync() }));
            app.MapPost("/next", async () => Results.Ok(new { ok = await media.NextAsync() }));
            app.MapPost("/previous", async () => Results.Ok(new { ok = await media.PreviousAsync() }));

            // Macros list
            app.MapGet("/macros", () => Results.Ok(macros.List()));

            // Run macro (accepts either { "id": "x" } OR { "Id": "x" })
            app.MapPost("/macros/run", async (HttpContext ctx) =>
            {
                var now = DateTime.UtcNow.Ticks;
                var last = Interlocked.Read(ref _lastMacroTicks);
                var elapsedMs = (now - last) / TimeSpan.TicksPerMillisecond;

                if (elapsedMs < MacroMinIntervalMs)
                    return Results.StatusCode(StatusCodes.Status429TooManyRequests);

                string body;
                try
                {
                    using var reader = new StreamReader(ctx.Request.Body);
                    body = (await reader.ReadToEndAsync()) ?? "";
                }
                catch
                {
                    return Results.BadRequest(new { ok = false, error = "Could not read body" });
                }

                if (string.IsNullOrWhiteSpace(body))
                    return Results.BadRequest(new { ok = false, error = "Empty body (no JSON received)" });

                string id = "";
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;

                    if (root.ValueKind != JsonValueKind.Object)
                        return Results.BadRequest(new { ok = false, error = "JSON must be an object" });

                    // Accept both "id" and "Id"
                    if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                        id = idEl.GetString() ?? "";
                    else if (root.TryGetProperty("Id", out var IdEl) && IdEl.ValueKind == JsonValueKind.String)
                        id = IdEl.GetString() ?? "";
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { ok = false, error = $"Invalid JSON: {ex.Message}" });
                }

                id = id.Trim();
                if (string.IsNullOrWhiteSpace(id))
                    return Results.BadRequest(new { ok = false, error = "Missing id" });

                try
                {
                    if (!macros.TryRun(id))
                        return Results.NotFound(new { ok = false, error = $"Unknown macro id: {id}" });

                    Interlocked.Exchange(ref _lastMacroTicks, now);
                    return Results.Ok(new { ok = true, id });
                }
                catch (Exception ex)
                {
                    // Return text so the phone can display it
                    return Results.Ok(new { ok = false, error = ex.ToString() });
                }
            });

            _app = app;
            await _app.StartAsync();
        }

        // Supports both casing styles from clients:
        private sealed class RunMacroReq
        {
            // camelCase
            public string? id { get; set; }

            // PascalCase
            public string? Id { get; set; }
        }

        public async Task StopAsync()
        {
            if (_app == null) return;
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }

        public void Dispose()
        {
            // Ensure the server is stopped synchronously when disposed from
            // synchronous contexts (e.g. window closing). Use GetAwaiter to
            // propagate exceptions to the caller instead of swallowing them.
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Swallow here to avoid throwing from Dispose; callers can use
                // DisposeAsync for async-aware disposal if they want errors.
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
        }
    }
}