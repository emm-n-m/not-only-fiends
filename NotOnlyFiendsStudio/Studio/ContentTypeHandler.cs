using System.Text.Json;

namespace NotOnlyFiendsStudio.Studio;

public abstract class ContentTypeHandler
{
    public string DirectoryName { get; }

    protected ContentTypeHandler(string directoryName)
    {
        DirectoryName = directoryName;
    }

    public abstract void LoadFromDirectory(string basePath, JsonSerializerOptions options);
    public abstract void LoadFromJson(string json, JsonSerializerOptions options);
}

public class ContentTypeHandler<T> : ContentTypeHandler where T : class
{
    private readonly Action<T> _register;

    public ContentTypeHandler(string directoryName, Action<T> register) : base(directoryName)
    {
        _register = register;
    }

    public override void LoadFromDirectory(string basePath, JsonSerializerOptions options)
    {
        var dir = Path.Combine(basePath, DirectoryName);
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
            LoadFromJson(File.ReadAllText(file), options);
    }

    public override void LoadFromJson(string json, JsonSerializerOptions options)
    {
        var items = JsonSerializer.Deserialize<List<T>>(json, options)
            ?? throw new InvalidOperationException($"Failed to deserialize list of {typeof(T).Name}");
        foreach (var item in items)
            _register(item);
    }
}
