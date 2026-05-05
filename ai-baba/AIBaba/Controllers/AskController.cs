using System.Diagnostics;
using AIBaba.Models;
using AIBaba.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIBaba.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public class AskController : ControllerBase
{
    private readonly IOllamaService _ollama;
    private readonly IMemoryStore _memory;
    private readonly ILogger<AskController> _log;

    public AskController(IOllamaService ollama, IMemoryStore memory, ILogger<AskController> log)
    {
        _ollama = ollama;
        _memory = memory;
        _log = log;
    }

    [HttpPost("ask")]
    public async Task<ActionResult<AskResponse>> Ask([FromBody] AskRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new AskResponse { Success = false, ErrorMessage = "message is required" });

        var sid = SessionMiddleware.GetSid(HttpContext);
        var profile = _memory.GetOrCreate(sid);

        // Apply profile updates that came with the request
        if (!string.IsNullOrWhiteSpace(req.Name))    _memory.SetName(sid, req.Name.Trim());
        if (!string.IsNullOrWhiteSpace(req.Avatar) || !string.IsNullOrWhiteSpace(req.Mindset))
            _memory.SetPreferences(sid, req.Avatar ?? profile.Avatar, req.Mindset ?? profile.Mindset);

        // Auto-extract a name from "my name is X" / "I'm X" if not yet stored
        TryExtractName(profile, req.Message, sid);

        var sw = Stopwatch.StartNew();
        try
        {
            var sys = PromptBuilder.BuildSystemPrompt(profile);
            var reply = await _ollama.ChatAsync(sys, profile.History, req.Message, ct);
            reply = (reply ?? "").Trim();
            if (string.IsNullOrEmpty(reply))
                reply = "Hmm, I have no answer for you right now, child. Try asking me again.";

            // Append both turns to memory
            _memory.Append(sid, new ChatMessage { Role = "user",      Content = req.Message });
            _memory.Append(sid, new ChatMessage { Role = "assistant", Content = reply });

            sw.Stop();
            return Ok(new AskResponse
            {
                Success = true,
                Reply = reply,
                Avatar = profile.Avatar,
                Mindset = profile.Mindset,
                Name = profile.Name,
                HistoryLength = profile.History.Count,
                ProcessingMs = sw.ElapsedMilliseconds
            });
        }
        catch (OllamaUnavailableException ex)
        {
            sw.Stop();
            _log.LogWarning(ex, "Ollama unavailable — returning friendly fallback");
            // Friendly fallback so the UI still demos voice + UI even if Ollama isn't running.
            var fallback = $"My mind is quiet right now — Ollama isn't responding at this moment. " +
                           $"Make sure Ollama is running locally and the model is pulled. ({ex.Message})";
            return Ok(new AskResponse
            {
                Success = false,
                Reply = fallback,
                ErrorMessage = ex.Message,
                Avatar = profile.Avatar,
                Mindset = profile.Mindset,
                Name = profile.Name,
                HistoryLength = profile.History.Count,
                ProcessingMs = sw.ElapsedMilliseconds
            });
        }
    }

    [HttpGet("profile")]
    public IActionResult GetProfile()
    {
        var sid = SessionMiddleware.GetSid(HttpContext);
        var p = _memory.GetOrCreate(sid);
        return Ok(new
        {
            sessionId = p.SessionId,
            name = p.Name,
            avatar = p.Avatar,
            mindset = p.Mindset,
            historyLength = p.History.Count
        });
    }

    [HttpPost("profile")]
    public IActionResult UpdateProfile([FromBody] ProfileUpdateRequest req)
    {
        var sid = SessionMiddleware.GetSid(HttpContext);
        if (!string.IsNullOrWhiteSpace(req.Name)) _memory.SetName(sid, req.Name.Trim());
        if (!string.IsNullOrWhiteSpace(req.Avatar) || !string.IsNullOrWhiteSpace(req.Mindset))
        {
            var profile = _memory.GetOrCreate(sid);
            _memory.SetPreferences(sid, req.Avatar ?? profile.Avatar, req.Mindset ?? profile.Mindset);
        }
        return Ok(new { success = true });
    }

    [HttpPost("reset")]
    public IActionResult Reset()
    {
        var sid = SessionMiddleware.GetSid(HttpContext);
        _memory.Reset(sid);
        return Ok(new { success = true });
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { ok = true, ts = DateTime.UtcNow });

    // ── helpers ──
    private void TryExtractName(UserProfile profile, string message, string sid)
    {
        if (!string.IsNullOrWhiteSpace(profile.Name)) return;
        var rxs = new[]
        {
            new System.Text.RegularExpressions.Regex(@"\bmy name is\s+([A-Z][a-zA-Z]+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            new System.Text.RegularExpressions.Regex(@"\bi am\s+([A-Z][a-zA-Z]+)\b",       System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            new System.Text.RegularExpressions.Regex(@"\bi'?m\s+([A-Z][a-zA-Z]+)\b",       System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        };
        foreach (var rx in rxs)
        {
            var m = rx.Match(message);
            if (m.Success)
            {
                var name = char.ToUpperInvariant(m.Groups[1].Value[0]) + m.Groups[1].Value[1..].ToLowerInvariant();
                _memory.SetName(sid, name);
                return;
            }
        }
    }
}
