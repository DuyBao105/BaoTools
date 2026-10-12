using System.IO;
using System.Text.Json;
using BaoToolsGui.Models;

namespace BaoToolsGui.Services;

/// <summary>
/// Manages the persistence and caching of app update notifications/releases as an update history.
/// Stored in %AppData%\BaoToolsGui\update-history.json.
/// </summary>
public class UpdateHistoryService
{
    private readonly string _historyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BaoToolsGui",
        "update-history.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] DefaultTagList = ["v10.5", "10.5"];

    public static bool IsDefaultTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;
        string norm = tag.Trim().TrimStart('v', 'V');
        return DefaultTagList.Any(d => string.Equals(d.TrimStart('v', 'V'), norm, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Default built-in releases so the update history in the bell is never blank even offline.
    /// Adapts title and body based on current UI culture (Vietnamese vs English/other).
    /// </summary>
    public static List<UpdateHistoryItem> GetDefaultHistory()
    {
        string lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        if (lang.Equals("vi", StringComparison.OrdinalIgnoreCase))
        {
            return new List<UpdateHistoryItem>
            {
                new()
                {
                    TagName = "v10.5",
                    Title = "BaoTools v10.5 - Phân loại Tải xuống (Game & Lua), Gỡ cài đặt an toàn & Đồng bộ 29 Ngôn ngữ",
                    Body = "• Phân loại Lịch sử tải xuống (Downloads History): Tách biệt trực quan giữa Game đã tải (Depot download) và Cấu hình Lua (Lua / Sadie / Hubcap / Manifest).\n• Bộ lọc Segmented Pills & Huy hiệu: Thanh lọc Tất cả, Game đã tải, Cấu hình Lua kèm số lượng đếm động thời gian thực và huy hiệu trực quan [Game] & [Cấu hình Lua].\n• Đổi Favicon Tổng đã tải: Cập nhật icon thư mục tải xuống FolderOpenDown chuẩn Fluent Design.\n• Cơ chế gỡ cài đặt an toàn: Sửa lỗi Uninstall chỉ gỡ bỏ thư mục game tải về trong Downloads/BaoTools Depots, tuyệt đối KHÔNG xóa file .lua cấu hình Steam.\n• Đồng bộ toàn diện 29 ngôn ngữ: Hoàn thiện 100% bản dịch và đồng bộ toàn bộ tài nguyên cho tất cả 29 ngôn ngữ trong dự án.",
                    PublishedAt = "05/10/2026",
                    HtmlUrl = "https://github.com/DevBaor/BaoTools_1005/releases/latest"
                }
            };
        }

        if (lang.Equals("zh", StringComparison.OrdinalIgnoreCase))
        {
            return new List<UpdateHistoryItem>
            {
                new()
                {
                    TagName = "v10.5",
                    Title = "BaoTools v10.5 - 下载历史智能分类（游戏与Lua配置）、安全卸载机制与全语言同步",
                    Body = "• 下载历史智能分类（Downloads History）：清晰分离游戏完整下载（Depot下载）与 Lua 配置文件（Lua / Sadie / Hubcap / Manifest）。\n• 分段药丸筛选与专属徽章：提供“全部”、“已下载游戏”、“Lua 配置”分类筛选，支持实时动态计数及专属 [游戏]、[Lua 配置] 视觉徽章。\n• 升级“总已下载”图标：换用符合 Fluent Design 规范的 FolderOpenDown 文件夹图标。\n• 安全卸载机制：修复卸载逻辑，卸载游戏仅安全移除 Downloads/BaoTools Depots 中的游戏文件，绝不误删 Steam 的 .lua 配置文件。\n• 29 种语言全面同步：补齐所有新增键值并 100% 同步全语言资源字典。",
                    PublishedAt = "2026-10-05",
                    HtmlUrl = "https://github.com/DevBaor/BaoTools_1005/releases/latest"
                }
            };
        }

        if (lang.Equals("ru", StringComparison.OrdinalIgnoreCase))
        {
            return new List<UpdateHistoryItem>
            {
                new()
                {
                    TagName = "v10.5",
                    Title = "BaoTools v10.5 - Категоризация загрузок (Игры и конфигурации Lua), безопасное удаление и синхронизация 29 языков",
                    Body = "• Категоризация истории загрузок (Downloads History): чёткое разделение между загрузками игр (Depot download) и конфигурационными файлами Lua (Lua / Sadie / Hubcap / Manifest).\n• Сегментированные фильтры и бейджи: фильтры «Все», «Загруженные игры», «Конфигурации Lua» с динамическим счетчиком и бейджами [Игра] и [Конфигурация Lua].\n• Новая иконка «Всего загружено»: обновлена на стильную папку FolderOpenDown в концепции Fluent Design.\n• Безопасный деинсталлятор: исправление удаления — деинсталляция удаляет только файлы загруженной игры в Downloads/BaoTools Depots и НИКОГДА не затрагивает файлы конфигурации .lua Steam.\n• Полная синхронизация 29 языков: добавлены и согласованы все недостающие ключи для всех 29 локализаций проекта.",
                    PublishedAt = "05.10.2026",
                    HtmlUrl = "https://github.com/DevBaor/BaoTools_1005/releases/latest"
                }
            };
        }

        return new List<UpdateHistoryItem>
        {
            new()
            {
                TagName = "v10.5",
                Title = "BaoTools v10.5 - Download Categorization (Games & Lua), Safe Uninstall & 29-Language Parity",
                Body = "• Download History Categorization: Clear distinction between full game downloads (Depot downloads) and Lua configurations (Lua / Sadie / Hubcap / Manifest).\n• Segmented Filter Pills & Visual Badges: Filter by All, Downloaded Games, and Lua Configurations with real-time dynamic item counts and dedicated [Game] & [Lua Config] badges.\n• Total Downloaded Favicon: Updated to modern Fluent Design FolderOpenDown icon.\n• Safe Uninstall Engine: Fixed uninstall to remove only downloaded game files in Downloads/BaoTools Depots, strictly NEVER deleting Steam .lua configuration files.\n• Comprehensive 29-Language Parity: 100% resource synchronization and translation across all 29 supported project languages.",
                PublishedAt = "Oct 05, 2026",
                HtmlUrl = "https://github.com/DevBaor/BaoTools_1005/releases/latest"
            }
        };
    }

    /// <summary>
    /// Loads update history from local disk or falls back to built-in defaults.
    /// Updates IsNew and IsCurrent based on currentVersion.
    /// </summary>
    public List<UpdateHistoryItem> LoadHistory(string currentVersion)
    {
        List<UpdateHistoryItem>? items = null;

        if (File.Exists(_historyPath))
        {
            try
            {
                string json = File.ReadAllText(_historyPath);
                items = JsonSerializer.Deserialize<List<UpdateHistoryItem>>(json, JsonOpts);
            }
            catch { /* corrupted or unreadable: fallback */ }
        }

        if (items is null || items.Count == 0)
        {
            items = GetDefaultHistory();
        }

        // Ensure defaults are merged and synchronized with current language
        var defaults = GetDefaultHistory();
        foreach (var def in defaults)
        {
            string defNorm = def.TagName.Trim().TrimStart('v', 'V');
            var existingItem = items.FirstOrDefault(x => string.Equals(x.TagName.Trim().TrimStart('v', 'V'), defNorm, StringComparison.OrdinalIgnoreCase));
            if (existingItem is null)
            {
                items.Add(def);
            }
            else
            {
                // Synchronize language for default items
                existingItem.TagName = def.TagName;
                existingItem.Title = def.Title;
                existingItem.Body = def.Body;
                existingItem.PublishedAt = def.PublishedAt;
                existingItem.HtmlUrl = def.HtmlUrl;
            }
        }

        // Deduplicate and filter out obsolete/legacy releases prior to v10.5
        var deduped = new List<UpdateHistoryItem>();
        var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
        {
            if (UpdateService.GetVersionScore(it.TagName) < UpdateService.GetVersionScore("v10.5"))
            {
                continue;
            }

            string norm = it.TagName.Trim().TrimStart('v', 'V');
            if (seenTags.Add(norm))
            {
                deduped.Add(it);
            }
        }
        items = deduped;

        UpdateItemFlags(items, currentVersion);
        var sorted = SortItems(items);
        SaveHistory(sorted);
        return sorted;
    }

    /// <summary>
    /// Saves items to disk.
    /// </summary>
    public void SaveHistory(IEnumerable<UpdateHistoryItem> items)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_historyPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(items, JsonOpts);
            File.WriteAllText(_historyPath, json);
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Merges remote GitHub releases into local history, updates flags, and saves to disk.
    /// </summary>
    public List<UpdateHistoryItem> MergeWithRemoteReleases(IEnumerable<GitHubReleaseInfo> remoteReleases, string currentVersion)
    {
        var existing = LoadHistory(currentVersion);
        var map = new Dictionary<string, UpdateHistoryItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in existing)
        {
            string k = it.TagName.Trim().TrimStart('v', 'V').ToLowerInvariant();
            map[k] = it;
        }

        foreach (var remote in remoteReleases)
        {
            if (string.IsNullOrWhiteSpace(remote.TagName)) continue;
            if (UpdateService.GetVersionScore(remote.TagName) < UpdateService.GetVersionScore("v10.5")) continue;

            string key = remote.TagName.Trim().TrimStart('v', 'V').ToLowerInvariant();

            string dateStr = remote.PublishedAt?.ToString("MMM dd, yyyy") ?? "";
            if (map.TryGetValue(key, out var item))
            {
                // Never overwrite curated localized content for default built-in items!
                if (!IsDefaultTag(item.TagName))
                {
                    item.Title = !string.IsNullOrWhiteSpace(remote.Title) ? remote.Title : item.Title;
                    item.Body = !string.IsNullOrWhiteSpace(remote.Body) ? remote.Body : item.Body;
                }
                item.HtmlUrl = !string.IsNullOrWhiteSpace(remote.HtmlUrl) ? remote.HtmlUrl : item.HtmlUrl;
                if (!string.IsNullOrWhiteSpace(dateStr)) item.PublishedAt = dateStr;
            }
            else
            {
                var newItem = new UpdateHistoryItem
                {
                    TagName = remote.TagName,
                    Title = string.IsNullOrWhiteSpace(remote.Title) ? remote.TagName : remote.Title,
                    Body = remote.Body,
                    HtmlUrl = remote.HtmlUrl,
                    PublishedAt = dateStr
                };
                map[key] = newItem;
                existing.Add(newItem);
            }
        }

        UpdateItemFlags(existing, currentVersion);
        var sorted = SortItems(existing);
        SaveHistory(sorted);
        return sorted;
    }

    private static void UpdateItemFlags(List<UpdateHistoryItem> items, string currentVersion)
    {
        string curNorm = currentVersion.Trim().TrimStart('v', 'V');
        foreach (var item in items)
        {
            string itemNorm = item.TagName.Trim().TrimStart('v', 'V');
            item.IsCurrent = string.Equals(itemNorm, curNorm, StringComparison.OrdinalIgnoreCase);
            item.IsNew = UpdateService.IsVersionNewer(item.TagName, currentVersion);
        }
    }

    private static List<UpdateHistoryItem> SortItems(List<UpdateHistoryItem> items)
    {
        return items.OrderByDescending(x => UpdateService.GetVersionScore(x.TagName)).ToList();
    }
}
