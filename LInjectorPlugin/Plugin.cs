using System.IO.Compression;
using LauncherGamePlugin;
using LauncherGamePlugin.Commands;
using LauncherGamePlugin.Forms;
using LauncherGamePlugin.Interfaces;
using Newtonsoft.Json;

namespace LInjectorPlugin;

public class Plugin : IGameSource
{
    public string ServiceName => "Legendary Injector";
    public string Version => "v1.0";
    public string SlugServiceName => "linjector";
    public string ShortServiceName => SlugServiceName;
    
    private string GetLegendaryPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "legendary");
    private Dictionary<string, InstalledJson> installedGames;
    public IApp App { get; private set; }
    
    public async Task<InitResult?> Initialize(IApp app)
    {
        await GetInstalledGames();
        App = app;
        return null;
    }

    public async Task<List<IGame>> GetGames()
    {
        await GetInstalledGames();
        return new();
    }

    public List<Command> GetGlobalCommands()
    {
        Command command = new("Dump Game", installedGames.Values.Select(x => new Command(x.Title, () => DumpGameMenu(x))).ToList());
        return new() { command, new("Install game via zip", ExtractGameMenu) };
    }

    private async Task GetInstalledGames()
    {
        string path = Path.Combine(GetLegendaryPath(), "installed.json");
        if (!File.Exists(path))
        {
            installedGames = new Dictionary<string, InstalledJson>();
            return;
        }
        
        installedGames = JsonConvert.DeserializeObject<Dictionary<string, InstalledJson>>(await File.ReadAllTextAsync(path))!;
    }

    private async Task SaveInstalledGames()
    {
        string path = Path.Combine(GetLegendaryPath(), "installed.json");
        await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(installedGames));
    }

    private void DumpGameMenu(InstalledJson installedJson)
    {
        App.ShowFolderPicker($"Dump {installedJson.Title} to?", "Destination folder", "Dump", x => DumpGame(installedJson, x));
    }

    private async void DumpGame(InstalledJson installedJson, string destPath)
    {
        App.ShowTextPrompt("Dumping game...");
        await GetInstalledGames();

        try
        {
            string installPath = installedJson.InstallPath;
            string installFilePath = Path.Join(installPath, "install.lin.json");
            string metadataFilePath = Path.Join(installPath, "meta.lin.json");
            object savePath = installedJson.SavePath;
            
            installedJson.InstallPath = "";
            installedJson.SavePath = null;
            await File.WriteAllTextAsync(installFilePath, JsonConvert.SerializeObject(installedJson));
            installedJson.InstallPath = installPath;
            installedJson.SavePath = savePath;
            
            if (File.Exists(metadataFilePath))
                File.Delete(metadataFilePath);

            File.Copy(Path.Join(GetLegendaryPath(), "metadata", $"{installedJson.AppName}.json"), metadataFilePath);

            string zipName = Path.GetInvalidFileNameChars().Any(x => installedJson.Title.Contains(x))
                ? $"{installedJson.AppName}.zip"
                : $"{installedJson.Title}.zip";
            
            await Task.Run(() => ZipFile.CreateFromDirectory(installPath, Path.Join(destPath, zipName)));
            App.ShowDismissibleTextPrompt($"Dumped {installedJson.Title} as {zipName} in {destPath}");
        }
        catch (Exception e)
        {
            App.ShowDismissibleTextPrompt($"Failed to dump game: {e.Message}");
        }
    }

    private void ExtractGameMenu()
    {
        App.ShowFilePicker("Select a game zip", "Game Zip Path", "Extract", ExtractGameValidation);
    }

    private async void ExtractGameValidation(string path)
    {
        // TODO: Extracting gives a partially filled in installed.json for some reason
        App.ShowTextPrompt("Extracting...");
        InstalledJson installedJson;
        try
        {
            if (!path.EndsWith(".zip"))
                throw new Exception("File is not a zip file");
            
            using (ZipArchive archive = ZipFile.OpenRead(path))
            {
                if (archive.Entries.All(x => x.Name != "meta.lin.json"))
                {
                    throw new Exception("Zip seemingly is not a valid Legendary Injector zip");
                }

                ZipArchiveEntry? installed = archive.Entries.FirstOrDefault(x => x.Name == "install.lin.json");
                StreamReader reader = new StreamReader(installed?.Open() ?? throw new Exception("Zip seemingly is not a valid Legendary Injector zip"));
                string text = await reader.ReadToEndAsync();
                installedJson = JsonConvert.DeserializeObject<InstalledJson>(text)!;
            }
        }
        catch (InvalidDataException _)
        {
            App.ShowDismissibleTextPrompt($"Failed to validate zip: Zip file is seemingly corrupt or invalid");
            return;
        }
        catch (Exception e)
        {
            App.ShowDismissibleTextPrompt($"Failed to validate zip: {e.Message}");
            return;
        }

        await GetInstalledGames();
        if (installedGames.Any(x => x.Key == installedJson.AppName))
        {
            App.ShowDismissibleTextPrompt("Game is already installed");
            return;
        }

        string installedPath = Path.Join(App.GameDir, "legendary-injector", installedJson.AppName);
        installedJson.InstallPath = installedPath;

        try
        {
            Directory.CreateDirectory(installedPath);
            await Task.Run(() => ZipFile.ExtractToDirectory(path, installedPath));
            installedGames.Add(installedJson.AppName, installedJson);
            await SaveInstalledGames();

            string metaSrcPath = Path.Join(installedPath, "meta.lin.json");
            string metaDstPath = Path.Join(GetLegendaryPath(), "metadata", $"{installedJson.AppName}.json");
            
            if (!File.Exists(metaDstPath))
                File.Copy(metaSrcPath, metaDstPath);
        }
        catch (Exception e)
        {
            App.ShowDismissibleTextPrompt($"Failed to extract zip: {e.Message}");
            return;
        }
        
        App.ReloadGames();
        App.ShowDismissibleTextPrompt($"Added {installedJson.Title} to library");
    }
}