using System;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.Core.Models;

namespace ProdControlAV.API.Services
{
    public sealed class DeviceCommandService : IDeviceCommandService
    {
        private readonly AppDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;

        // keep JSON minimal & safe
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = false
        };

        public DeviceCommandService(AppDbContext db, IHttpClientFactory httpClientFactory)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<DeviceCommandResult> ExecuteDeviceActionAsync(Guid commandId, Guid userTenantId, CancellationToken ct = default)
        {
            // Load the DeviceAction with Device, filtered by user's tenant (if applicable in your schema)
            // ---- Tweak these property names to match your model ----
            var action = await _db.DeviceActions
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.ActionId == commandId && a.TenantId == userTenantId, ct);
            if (action == null)
                return new DeviceCommandResult(false, 404, $"CommandId {commandId} not found.", null);

            var device = await _db.Devices
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == action.DeviceId && d.TenantId == userTenantId, ct);
            if (device == null)
                return new DeviceCommandResult(false, 404, "Device not found for command.", null);

            // Validate IP to mitigate SSRF: only allow private RFC1918 or link-local if you intend
            if (!IPAddress.TryParse(device.Ip, out var ip) || !IsPrivate(ip))
                return new DeviceCommandResult(false, null, "Device IP is invalid or not allowed.", null);

            // Build URI
            var port = device.Port is int p && p > 0 && p < 65536 ? p : 80; // adjust defaults as needed
            var baseUri = new Uri($"http://{device.Ip}:{port}/");

            // Action path/method/payload
            var path = NormalizePath(action.Command ?? ""); // e.g. "api/power/on"
            var method = NormalizeMethod(action.HttpMethod ?? "POST");

            using var req = new HttpRequestMessage(new HttpMethod(method), new Uri(baseUri, path));

            // Add any action-specific headers if you store them (e.g., Basic auth token). Example:
            // if (!string.IsNullOrEmpty(action.AuthHeader)) req.Headers.TryAddWithoutValidation("Authorization", action.AuthHeader);

            var client = _httpClientFactory.CreateClient("device-commands");
            client.Timeout = TimeSpan.FromSeconds(5); // short timeout—your dashboard is “near real-time”

            try
            {
                // Optional: include correlation id
                req.Headers.TryAddWithoutValidation("X-CommandId", commandId.ToString());

                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                var success = resp.IsSuccessStatusCode;

                // Optional: persist audit log (create a table later if desired)
                // await _db.CommandLogs.AddAsync(new CommandLog {...}); await _db.SaveChangesAsync(ct);

                return new DeviceCommandResult(success, (int)resp.StatusCode,
                    success ? "Command executed." : "Device responded with an error.",
                    Truncate(body, 2000));
            }
            catch (TaskCanceledException)
            {
                return new DeviceCommandResult(false, null, "Timed out contacting device.", null);
            }
            catch (Exception ex)
            {
                return new DeviceCommandResult(false, null, $"Failed to contact device: {ex.Message}", null);
            }

            // Helpers

            static bool IsPrivate(IPAddress ipAddr)
            {
                if (ipAddr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false; // IPv4 only; extend if you allow v6
                var bytes = ipAddr.GetAddressBytes();
                // 10.0.0.0/8
                if (bytes[0] == 10) return true;
                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168) return true;
                // 169.254.0.0/16 (link-local) - enable only if you expect it
                // if (bytes[0] == 169 && bytes[1] == 254) return true;
                return false;
            }

            static string NormalizePath(string p)
            {
                p = p.Trim();
                if (p.StartsWith("/")) p = p[1..];
                return p;
            }

            static string NormalizeMethod(string m)
            {
                m = (m ?? "POST").Trim().ToUpperInvariant();
                return m switch
                {
                    "GET" or "POST" or "PUT" or "DELETE" or "PATCH" => m,
                    _ => "POST"
                };
            }

            static string? Truncate(string? s, int max)
            {
                if (s == null) return null;
                return s.Length <= max ? s : s[..max];
            }
        }
    }
}
