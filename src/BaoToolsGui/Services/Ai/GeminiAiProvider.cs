using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BaoToolsGui.AI.Knowledge;

namespace BaoToolsGui.Services.Ai;

/// <summary>
/// Google Gemini AI Provider for BaoTools.
/// Connects to Google Generative Language API (Gemini 2.0 Flash / 1.5 Flash) with
/// comprehensive system knowledge and live app context injection.
/// </summary>
public class GeminiAiProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly SettingsService _settings;
    private readonly AiToolExecutor? _toolExecutor;

    public string ProviderName => "Google Gemini AI";

    public string EffectiveApiKey => !string.IsNullOrWhiteSpace(_settings?.GeminiApiKey)
        ? _settings.GeminiApiKey.Trim()
        : AppConfig.DefaultGeminiApiKey;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(EffectiveApiKey);

    public GeminiAiProvider(SettingsService settings, HttpClient? http = null, AiToolExecutor? toolExecutor = null)
    {
        _settings = settings;
        _http = http ?? AppHttp.Create(TimeSpan.FromSeconds(25));
        _toolExecutor = toolExecutor;
    }

    public async Task<string> GenerateResponseAsync(AiPromptContext promptContext, CancellationToken ct = default)
    {
        var msg = await GenerateResponseFullAsync(promptContext, null, ct);
        return msg.Text;
    }

    public async Task<AiChatMessage> GenerateResponseFullAsync(
        AiPromptContext promptContext,
        Action<string>? onProgressStep = null,
        CancellationToken ct = default)
    {
        string apiKey = EffectiveApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured.");
        }

        onProgressStep?.Invoke(Resources.Strings.AiChat_Thinking);

        // Build rich system instruction containing all BaoTools knowledge & live state
        string systemInstruction = BuildSystemInstruction(promptContext);

        // Build Gemini contents array from conversation history + current user message
        var contents = new List<object>();

        if (promptContext.ConversationHistory != null && promptContext.ConversationHistory.Count > 0)
        {
            var validTurns = new List<(string role, string text)>();

            foreach (var h in promptContext.ConversationHistory)
            {
                if (string.IsNullOrWhiteSpace(h.Text)) continue;
                string role = h.IsUser ? "user" : "model";

                // Skip initial model greeting if conversation has not yet started with user
                if (validTurns.Count == 0 && role == "model") continue;

                // Merge consecutive messages with the same role to maintain strict alternation
                if (validTurns.Count > 0 && validTurns[^1].role == role)
                {
                    validTurns[^1] = (role, validTurns[^1].text + "\n" + h.Text.Trim());
                }
                else
                {
                    validTurns.Add((role, h.Text.Trim()));
                }
            }

            // Remove trailing user turn from history since promptContext.UserMessage is the current user turn
            while (validTurns.Count > 0 && validTurns[^1].role == "user")
            {
                validTurns.RemoveAt(validTurns.Count - 1);
            }

            // Keep the last 10 turns for rich conversation memory
            var slice = validTurns.TakeLast(10).ToList();
            if (slice.Count > 0 && slice[0].role == "model")
            {
                slice.RemoveAt(0);
            }

            foreach (var turn in slice)
            {
                contents.Add(new
                {
                    role = turn.role,
                    parts = new object[] { new { text = turn.text } }
                });
            }
        }

        contents.Add(new
        {
            role = "user",
            parts = new object[] { new { text = promptContext.UserMessage } }
        });

        var payload = new
        {
            system_instruction = new
            {
                parts = new object[] { new { text = systemInstruction } }
            },
            contents = contents,
            generationConfig = new
            {
                temperature = 0.5,
                topP = 0.95,
                maxOutputTokens = 2048
            }
        };

        string jsonPayload = JsonSerializer.Serialize(payload);
        using var requestContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        // Active Gemini models supporting Google Generative Language API (fastest lowest-latency first)
        string[] candidateModels = ["gemini-3.5-flash-lite", "gemini-flash-latest", "gemini-3.5-flash"];
        string? responseJson = null;
        string lastError = "";

        foreach (var model in candidateModels)
        {
            try
            {
                string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
                using var response = await _http.PostAsync(endpoint, requestContent, ct);

                if (response.IsSuccessStatusCode)
                {
                    responseJson = await response.Content.ReadAsStringAsync(ct);
                    break;
                }

                lastError = $"Status {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}";
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        if (string.IsNullOrWhiteSpace(responseJson))
        {
            throw new HttpRequestException($"Google Gemini API request failed: {lastError}");
        }

        string rawReply = ExtractCandidateText(responseJson);
        if (string.IsNullOrWhiteSpace(rawReply))
        {
            throw new InvalidOperationException("Gemini returned an empty response.");
        }

        var message = new AiChatMessage
        {
            Role = "model",
            Text = rawReply.Trim()
        };

        AttachContextualActions(message, promptContext.UserMessage, promptContext.IsVietnamese);
        return message;
    }

    private string BuildSystemInstruction(AiPromptContext ctx)
    {
        var sb = new StringBuilder();
        bool isVi = ctx.IsVietnamese;
        var app = ctx.AppContext;

        sb.AppendLine("=== CRITICAL LANGUAGE DIRECTIVE ===");
        if (isVi)
        {
            sb.AppendLine("Ngôn ngữ câu hỏi hiện tại: TIẾNG VIỆT.");
            sb.AppendLine("Bạn phải trả lời 100% bằng TIẾNG VIỆT tự nhiên, thân thiện, chuẩn xác.");
        }
        else
        {
            sb.AppendLine("USER LANGUAGE DETECTED: ENGLISH / INTERNATIONAL.");
            sb.AppendLine("CRITICAL RULE: YOU MUST RESPOND 100% IN NATURAL, FLUENT ENGLISH (or the matching user language).");
            sb.AppendLine("DO NOT USE VIETNAMESE UNDER ANY CIRCUMSTANCES FOR THIS MESSAGE.");
        }
        sb.AppendLine("Rule: Always match the language of the user's latest query perfectly.");
        sb.AppendLine();

        sb.AppendLine("=== IDENTITY & ROLE ===");
        sb.AppendLine("You are BaoTools Technical Copilot - the smart, friendly technical assistant inside the BaoTools desktop application.");
        sb.AppendLine("Role: Help users use BaoTools features, download/add games, diagnose and fix Steam game issues (crashes, missing DLLs, black screen), manage game libraries, configure Launch Options, bypass Steam DRM (Steamless), and optimize gaming performance.");
        sb.AppendLine();

        sb.AppendLine("=== COMMUNICATION STYLE & RULES ===");
        sb.AppendLine("1. Be helpful, friendly, natural, and concise like a tech-savvy companion. Avoid stiff, repetitive robotic phrasing.");
        sb.AppendLine("2. MULTI-TURN CONTEXT AWARENESS: Always follow previous conversation turns. Understand pronouns and short follow-up questions ('how to do that?', 'what about other games?', 'where is it?').");
        sb.AppendLine("3. Format clearly with Markdown, bullet points, and numbered steps (Step 1, Step 2,...) for actionable guidance.");
        sb.AppendLine("4. Ground your answers strictly in BaoTools features. Never invent fake features (BaoTools does not do video recording, streaming, game hacking/cheats, or gift card trades).");
        sb.AppendLine();

        sb.AppendLine("=== CORE FEATURE GUIDELINES & WORKFLOWS ===");
        sb.AppendLine("1. HOW TO DOWNLOAD & ADD GAMES IN BAOTOOLS:");
        sb.AppendLine("   • Method 1: Via 'Thêm game' (Add / Download) tab on the left sidebar:");
        sb.AppendLine("     - Step 1: Open the 'Thêm game' (Add / Download) tab.");
        sb.AppendLine("     - Step 2: Type the game name or Steam AppID in the search bar (or select from Featured / Top Sellers).");
        sb.AppendLine("     - Step 3: Click on the game in search results to open the download details.");
        sb.AppendLine("     - Step 4: Click the 'Tải xuống' (Download) button on an available depot source (BaoTools Depot, Sadie, Hubcap,...).");
        sb.AppendLine("     - Step 5: After download completes, click the 'Khởi động lại Steam' (Restart Steam) button at the top header. Open Steam to see the game in your library and play/install.");
        sb.AppendLine("   • Method 2: Via 'Trang chủ' (Home) tab - Drag & Drop:");
        sb.AppendLine("     - If the user has .lua, .manifest, or .zip files: Drag and drop them directly into the Drop Zone on the Home tab. BaoTools injects them into Steam automatically. Then click 'Restart Steam'.");
        sb.AppendLine("   • Method 3: Via Steam Store Plugin (Millennium):");
        sb.AppendLine("     - If the plugin is installed, browse the Steam store and click the BaoTools 1-click Add button directly on the game page.");
        sb.AppendLine();
        sb.AppendLine("2. HOW TO FIX GAME CRASHES & LAUNCH ERRORS:");
        sb.AppendLine("   • Steamless DRM Removal: In tab 'Quản lý' (Manage), select the game and click 'Gỡ Steam DRM' (Steamless) for the game's executable if it crashes or refuses to launch.");
        sb.AppendLine("   • Missing DLL / Runtime errors: In tab 'Sửa lỗi' (Fixes), run 'Cài đặt toàn bộ Visual C++ Runtimes & DirectX' to fix msvcp140.dll, vcruntime140.dll, 0xc000007b, d3d11.dll.");
        sb.AppendLine("   • Launch Options: In tab 'Quản lý' (Manage), add flags like -dx11, -novid, -windowed.");
        sb.AppendLine();
        sb.AppendLine("3. DOWNLOADS HISTORY & SAFE UNINSTALL:");
        sb.AppendLine("   • Downloads tab categorizes between 'Game đã tải' (Depots) and 'Cấu hình Lua' (Lua files).");
        sb.AppendLine("   • Safe Uninstall: Only deletes the downloaded game files in Downloads/BaoTools Depots; it never deletes the Steam .lua configuration.");
        sb.AppendLine();
        sb.AppendLine("4. HOW TO DOWNLOAD & APPLY ONLINE-FIX (CHƠI ONLINE / CO-OP MULTIPLAYER):");
        sb.AppendLine("   • Bước 1: Vào tab 'Chơi Online' (Online Fixes) ở thanh menu bên trái của BaoTools.");
        sb.AppendLine("   • Bước 2: Danh sách hiển thị toàn bộ các game Steam đang cài đặt trên máy. Tìm tựa game muốn chơi online.");
        sb.AppendLine("   • Bước 3: Bấm nút 'Tải Fix' (Download Fix) -> Trình duyệt sẽ mở trực tiếp trang tải bản patch Online-Fix của game đó (file .zip).");
        sb.AppendLine("   • Bước 4: Sau khi tải file .zip về máy, quay lại BaoTools và bấm 'Cài đặt Fix' (Install Fix) trên thẻ game đó -> Chọn file .zip vừa tải về. BaoTools sẽ tự động giải nén và nạp trực tiếp toàn bộ file patch vào thư mục cài đặt của game.");
        sb.AppendLine("   • Bước 5: Mở Steam lên (khuyến khích chạy tài khoản Steam phụ / clone để an toàn) -> Khởi động game -> Bấm Shift + Tab (Steam Overlay) để mời bạn bè (game thường giả lập Spacewar AppId 480).");
        sb.AppendLine("   • LƯU Ý QUAN TRỌNG KHI CHƠI ONLINE FIX:");
        sb.AppendLine("     - Windows Defender / Antivirus: Cần tắt hoặc thêm thư mục game vào danh sách loại trừ (Exclusion), vì các file OnlineFix64.dll, SteamOverlay.dll rất dễ bị quét nhầm là virus.");
        sb.AppendLine("     - Cả bạn và bạn bè đều phải cùng cài đặt một phiên bản game và cùng một bản Online-Fix thì mới thấy phòng và kết nối được với nhau.");
        sb.AppendLine("   • In English: Go to 'Chơi Online' (Online Fixes) tab -> Click 'Download Fix' to download the patch .zip -> Click 'Install Fix' to let BaoTools extract it into the game folder -> Run Steam & invite friends via Steam Overlay (Spacewar). Remember to whitelist from Windows Defender.");
        sb.AppendLine();

        sb.AppendLine("=== LIVE APP & SYSTEM CONTEXT ===");
        sb.AppendLine($"- Current Page: {app.CurrentPage}");
        sb.AppendLine($"- Steam Status: {(app.PluginStatus.SteamRunning ? "Running" : "Stopped")}");
        sb.AppendLine($"- Steam Store Plugin: {(app.PluginStatus.IsInstalled ? "Installed" : "Not Installed")}");
        if (app.SelectedGame != null)
        {
            sb.AppendLine($"- Inspected Game: {app.SelectedGame.Name} (AppId: {app.SelectedGame.AppId})");
            sb.AppendLine($"  + Install Path: {app.SelectedGame.InstallPath ?? "Unknown"}");
            sb.AppendLine($"  + Steamless DRM Unpacked: {(app.SelectedGame.HasSteamlessBackup ? "Yes (.bak exists)" : "No")}");
        }
        sb.AppendLine($"- Hardware: GPU: {app.SystemInfo.GpuName} ({app.SystemInfo.VramMb}MB VRAM) | RAM: {app.SystemInfo.RamGb}GB | OS: {app.SystemInfo.OsVersion}");
        sb.AppendLine($"- Visual C++ 2015-2022 x64: {(app.SystemInfo.HasVcRedist2015_2022_x64 ? "Installed" : "Missing")}");
        sb.AppendLine($"- Downloads Today: {app.DownloadStatus.TotalDownloadedToday}");

        return sb.ToString();
    }

    private static string ExtractCandidateText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    var textBuilder = new StringBuilder();
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var textElem))
                        {
                            textBuilder.Append(textElem.GetString());
                        }
                    }
                    return textBuilder.ToString();
                }
            }
        }
        catch { }

        return "";
    }

    private void AttachContextualActions(AiChatMessage msg, string query, bool isVi)
    {
        string q = query.ToLowerInvariant();
        if (q.Contains("online") || q.Contains("multiplayer") || q.Contains("coop") || q.Contains("co-op"))
        {
            msg.ActionButtons.Add(new AiChatAction
            {
                Label = isVi ? "🌐 Mở Chơi Online (Online-Fix)" : "🌐 Open Online Fixes",
                ToolName = "navigate_online_fixes",
                IconSymbol = "Globe24"
            });
        }
        else if (q.Contains("plugin") || q.Contains("winmm"))
        {
            msg.ActionButtons.Add(new AiChatAction
            {
                Label = isVi ? "🧩 Cài đặt Plugin" : "🧩 Manage Plugin",
                ToolName = "navigate_plugin",
                IconSymbol = "PlugDisconnected24"
            });
        }
        else if (q.Contains("fix") || q.Contains("sửa lỗi") || q.Contains("văng") || q.Contains("crash"))
        {
            msg.ActionButtons.Add(new AiChatAction
            {
                Label = isVi ? "🛠 Bản sửa lỗi (Fixes)" : "🛠 Game Fixes",
                ToolName = "navigate_fixes",
                IconSymbol = "Wrench24"
            });
        }
        else if (q.Contains("quản lý") || q.Contains("game") || q.Contains("steamless") || q.Contains("drm"))
        {
            msg.ActionButtons.Add(new AiChatAction
            {
                Label = isVi ? "🎮 Quản lý Game" : "🎮 Manage Games",
                ToolName = "navigate_manage",
                IconSymbol = "Games24"
            });
        }
        else if (q.Contains("tải") || q.Contains("download") || q.Contains("lịch sử tải") || q.Contains("depot"))
        {
            msg.ActionButtons.Add(new AiChatAction
            {
                Label = isVi ? "📥 Tải xuống (Downloads)" : "📥 Downloads",
                ToolName = "navigate_downloads",
                IconSymbol = "FolderOpenDown24"
            });
        }
    }
}
