using Sandbox.ModAPI;
using Scripts.BlockCulling;
using System.IO;
using System;

public class ModConfig
{
    public bool ModEnabled { get; set; } = true; // Default enabled
    private const string CONFIG_FILE_NAME = "BlockCullingConfig.cfg";  // Notice .cfg instead of .xml

    public static ModConfig Load()
    {
        var config = new ModConfig();
        if (MyAPIGateway.Utilities.FileExistsInLocalStorage(CONFIG_FILE_NAME, typeof(ModConfig)))
        {
            try
            {
                using (TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(CONFIG_FILE_NAME, typeof(ModConfig)))
                {
                    string text = reader.ReadToEnd();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string line in lines)
                        {
                            string[] parts = line.Split('=');
                            if (parts.Length == 2)
                            {
                                if (parts[0].Trim() == "ModEnabled")
                                {
                                    bool value;
                                    if (bool.TryParse(parts[1].Trim(), out value))
                                        config.ModEnabled = value;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ThreadSafeLog.EnqueueMessage($"Failed loading config: {e.Message}. Using defaults.");
            }
        }
        return config;
    }

    public void Save()
    {
        try
        {
            using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(CONFIG_FILE_NAME, typeof(ModConfig)))
            {
                writer.WriteLine($"ModEnabled={ModEnabled}");
                writer.Flush();
            }
        }
        catch (Exception e)
        {
            ThreadSafeLog.EnqueueMessage($"Failed saving config: {e.Message}");
        }
    }
}