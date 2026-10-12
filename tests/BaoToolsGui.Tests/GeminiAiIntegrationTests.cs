using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BaoToolsGui.AI.Discovery;
using BaoToolsGui.Services;
using BaoToolsGui.Services.Ai;
using BaoToolsGui.ViewModels;
using Xunit;

namespace BaoToolsGui.Tests;

public class GeminiAiIntegrationTests : IDisposable
{
    private readonly string _tempSettingsPath;

    public GeminiAiIntegrationTests()
    {
        _tempSettingsPath = Path.Combine(Path.GetTempPath(), $"baotools_gemini_test_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempSettingsPath)) File.Delete(_tempSettingsPath);
            string bak = _tempSettingsPath + ".bak";
            if (File.Exists(bak)) File.Delete(bak);
            string tmp = _tempSettingsPath + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        catch { }
    }

    [Fact]
    public void AppConfig_ContainsBuiltInGeminiApiKey()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.DefaultGeminiApiKey));
        Assert.StartsWith("AQ.", AppConfig.DefaultGeminiApiKey);
    }

    [Fact]
    public void SettingsService_SavesAndRetrievesGeminiApiKey()
    {
        var settings = new SettingsService(_tempSettingsPath);
        Assert.Null(settings.GeminiApiKey);

        settings.GeminiApiKey = "AIzaSyTestKey12345";
        Assert.Equal("AIzaSyTestKey12345", settings.GeminiApiKey);

        // Reload from disk
        var reloaded = new SettingsService(_tempSettingsPath);
        Assert.Equal("AIzaSyTestKey12345", reloaded.GeminiApiKey);

        // Clear key
        settings.GeminiApiKey = "";
        Assert.Null(settings.GeminiApiKey);
    }

    [Fact]
    public void GeminiAiProvider_UsesHardcodedApiKeyByDefault()
    {
        var settings = new SettingsService(_tempSettingsPath);
        var provider = new GeminiAiProvider(settings);

        // By default, uses built-in AppConfig.DefaultGeminiApiKey
        Assert.True(provider.IsAvailable);
        Assert.Equal(AppConfig.DefaultGeminiApiKey, provider.EffectiveApiKey);

        // Custom key override
        settings.GeminiApiKey = "AIzaSyCustomKey";
        Assert.Equal("AIzaSyCustomKey", provider.EffectiveApiKey);
    }

    [Fact]
    public async Task AiChatService_FallsBackToLocalExpertWhenGeminiFailsOrQuotaExhausted()
    {
        var settings = new SettingsService(_tempSettingsPath);
        // Set an invalid key to simulate quota or network failure
        settings.GeminiApiKey = "INVALID_KEY_TEST";

        var steam = new SteamService(settings);
        var vault = new LuaVault(steam);
        var service = new AiChatService(settings, steam, vault);

        var history = new List<AiChatMessage>();
        // Query that local expert knows
        var response = await service.SendMessageFullAsync(history, "xin chÃ o bot");

        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response.Text));
    }

    [Fact]
    public async Task LocalExpert_AnswersGeminiInquiryWithAction()
    {
        var localExpert = new LocalExpertAiProvider();

        var promptContextVi = new AiPromptContext
        {
            UserMessage = "LÃ m sao Ä‘á»ƒ dÃ¹ng Gemini trong BaoTools?",
            IsVietnamese = true
        };

        var responseVi = await localExpert.GenerateResponseFullAsync(promptContextVi);
        Assert.Contains("Google Gemini AI", responseVi.Text);

        var promptContextEn = new AiPromptContext
        {
            UserMessage = "How to configure Gemini api key?",
            IsVietnamese = false
        };

        var responseEn = await localExpert.GenerateResponseFullAsync(promptContextEn);
        Assert.Contains("Google Gemini AI", responseEn.Text);
    }

    [Fact]
    public void CodebaseScanner_IncludesNewV10_5Features()
    {
        var manifest = CodebaseScanner.ScanSystem();

        Assert.Contains(manifest.Features, f => f.Id == "gemini_ai_assistant");
        Assert.Contains(manifest.Features, f => f.Id == "downloads_categorization");
        Assert.Contains(manifest.Features, f => f.Id == "safe_uninstall");
        Assert.Contains(manifest.Settings, s => s.Property == "GeminiApiKey");
    }
}
