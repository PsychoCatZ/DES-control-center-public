using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DesControlCenter.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace DesControlCenter.Infrastructure.Notifications;

/// <summary>
/// Sends messages to the owner's own chat through the owner's own bot. The bot token is a secret:
/// it is put into <c>telegram-token.txt</c> once, and on first start is moved into a file encrypted
/// with Windows DPAPI (readable only by this Windows user on this PC) while the plain-text file is
/// deleted. The chat id (not a secret) lives in <c>telegram.json</c>.
/// </summary>
public sealed class TelegramSender : ITelegramSender
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly ILogger<TelegramSender> _logger;
    private readonly string _dir;
    private string? _token;
    private long? _chatId;
    private bool _loaded;

    public TelegramSender(ILogger<TelegramSender> logger, string? directory = null)
    {
        _logger = logger;
        _dir = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesControlCenter");
    }

    public bool IsConfigured
    {
        get
        {
            Load();
            return _token is not null && _chatId is not null;
        }
    }

    public async Task<bool> SendAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return false;
        }

        try
        {
            using var response = await Client.PostAsJsonAsync(
                $"https://api.telegram.org/bot{_token}/sendMessage",
                new { chat_id = _chatId, text, disable_web_page_preview = true },
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Status code only — the URL contains the token, so never log it.
                _logger.LogWarning("Telegram sendMessage returned {Status}", (int)response.StatusCode);
            }

            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Telegram send failed: {Type}", ex.GetType().Name);
            return false;
        }
    }

    private void Load()
    {
        if (_loaded)
        {
            return;
        }

        try
        {
            _token = LoadToken();
            _chatId = LoadChatId();
            _loaded = _token is not null && _chatId is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Telegram settings could not be loaded: {Type}", ex.GetType().Name);
        }
    }

    private string? LoadToken()
    {
        var encrypted = Path.Combine(_dir, "telegram-token.bin");
        var plain = Path.Combine(_dir, "telegram-token.txt");

        if (File.Exists(plain))
        {
            var token = File.ReadAllText(plain).TrimStart('﻿').Trim();
            if (token.Length > 0)
            {
                File.WriteAllBytes(encrypted, Dpapi.Protect(Encoding.UTF8.GetBytes(token)));

                // Delete the plain copy only once the encrypted one demonstrably decrypts to the same value.
                if (Decrypt(encrypted) == token)
                {
                    File.Delete(plain);
                }

                return token;
            }
        }

        return File.Exists(encrypted) ? Decrypt(encrypted) : null;
    }

    private static string Decrypt(string path) =>
        Encoding.UTF8.GetString(Dpapi.Unprotect(File.ReadAllBytes(path)));

    private long? LoadChatId()
    {
        var path = Path.Combine(_dir, "telegram.json");
        if (!File.Exists(path))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.TryGetProperty("ChatId", out var id) && id.TryGetInt64(out var value) ? value : null;
    }
}
