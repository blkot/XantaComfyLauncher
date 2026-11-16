namespace XantaComfyLauncher.Utils;

public static class AppIcon
{
    public static Icon? Load()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            return null;
        }
    }
}
