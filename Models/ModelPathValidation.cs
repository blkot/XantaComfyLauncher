namespace XantaComfyLauncher.Models;

public sealed class ModelPathValidation
{
    public string Category { get; set; } = "";
    public string Path { get; set; } = "";
    public bool Exists { get; set; }
    public bool Readable { get; set; }
    public string Detail { get; set; } = "";
}
