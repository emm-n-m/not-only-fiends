using System.Text.Json;
using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;
using Microsoft.JSInterop;

namespace NotOnlyFiendsFeed.Services;

public class BrowserFileService
{
    private readonly IJSRuntime _js;

    public BrowserFileService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task DownloadCharacterAsync(Character character)
    {
        var json = JsonSerializer.Serialize(character, JsonOptions.Default);
        var filename = $"{character.Name.Trim().Replace(" ", "_").ToLowerInvariant()}.json";
        await _js.InvokeVoidAsync("fileHelpers.downloadJson", filename, json);
    }

    public async Task DownloadPcgAsync(string filename, string content)
    {
        await _js.InvokeVoidAsync("fileHelpers.downloadText", filename, content, "application/x-pcgen");
    }

    public async Task<Character?> OpenCharacterAsync()
    {
        var json = await _js.InvokeAsync<string?>("fileHelpers.openJsonFile", "file-open-input");
        if (string.IsNullOrEmpty(json)) return null;
        return JsonSerializer.Deserialize<Character>(json, JsonOptions.Default);
    }

    public async Task<PcgFileData?> OpenPcgFileAsync()
    {
        return await _js.InvokeAsync<PcgFileData?>("fileHelpers.openPcgFile", "pcg-file-input");
    }

    public class PcgFileData
    {
        public string? Name { get; set; }
        public string? Content { get; set; }
        public bool UsedLegacyEncoding { get; set; }
    }
}
