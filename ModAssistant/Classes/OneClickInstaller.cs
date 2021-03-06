using Microsoft.Win32;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using static ModAssistant.Http;

namespace ModAssistant
{
    class OneClickInstaller
    {
        private const string ModelSaberURLPrefix = "https://modelsaber.com/files/";
        private const string BeatSaverURLPrefix = "https://beatsaver.com";

        private static readonly string BeatSaberPath = App.BeatSaberInstallDirectory;

        private const string CustomAvatarsFolder = "CustomAvatars";
        private const string CustomSabersFolder = "CustomSabers";
        private const string CustomPlatformsFolder = "CustomPlatforms";
        private static readonly string CustomSongsFolder = Path.Combine("Beat Saber_Data", "CustomLevels");

        private static readonly string[] Protocols = new[] { "modelsaber", "beatsaver" };

        private const bool BypassDownloadCounter = false;
        public static async void InstallAsset(string link)
        {
            Uri uri = new Uri(link);
            if (!Protocols.Contains(uri.Scheme)) return;

            switch (uri.Scheme)
            {
                case "modelsaber":
                    await ModelSaber(uri);
                    break;
                case "beatsaver":
                    await BeatSaver(uri);
                    break;
            }
        }

        private static async Task BeatSaver(Uri uri)
        {
            string Key = uri.Host;
            BeatSaverApiResponse Response;

            try
            {
                var resp = await HttpClient.GetAsync(BeatSaverURLPrefix + "/api/maps/detail/" + Key);
                var body = await resp.Content.ReadAsStringAsync();

                Response = JsonSerializer.Deserialize<BeatSaverApiResponse>(body);
            }
            catch (Exception e)
            {
                MessageBox.Show($"{Application.Current.FindResource("OneClick:MapDownloadFailed")}\n\n" + e);
                return;
            }

            string zip = Path.Combine(BeatSaberPath, CustomSongsFolder, Response.hash) + ".zip";
            string directory = Path.Combine(
                BeatSaberPath,
                CustomSongsFolder,
                string.Concat(
                    (Response.key + " (" + Response.metadata.songName + " - " + Response.metadata.levelAuthorName + ")")
                    .Split(Utils.Constants.IllegalCharacters)
                )
            );

            if (BypassDownloadCounter)
            {
                await DownloadAsset(BeatSaverURLPrefix + Response.directDownload, CustomSongsFolder, Response.hash + ".zip");
            }
            else
            {
                await DownloadAsset(BeatSaverURLPrefix + Response.downloadURL, CustomSongsFolder, Response.hash + ".zip");
            }

            if (File.Exists(zip))
            {
                using (FileStream stream = new FileStream(zip, FileMode.Open))
                using (ZipArchive archive = new ZipArchive(stream))
                {
                    foreach (ZipArchiveEntry file in archive.Entries)
                    {
                        string fileDirectory = Path.GetDirectoryName(Path.Combine(directory, file.FullName));
                        if (!Directory.Exists(fileDirectory))
                        {
                            Directory.CreateDirectory(fileDirectory);
                        }

                        if (!string.IsNullOrEmpty(file.Name))
                        {
                            file.ExtractToFile(Path.Combine(directory, file.FullName), true);
                        }
                    }
                }

                File.Delete(zip);
            }
            else
            {
                string line1 = (string)Application.Current.FindResource("OneClick:SongDownload:Failed");
                string line2 = (string)Application.Current.FindResource("OneClick:SongDownload:NetworkIssues");
                string title = (string)Application.Current.FindResource("OneClick:SongDownload:FailedTitle");
                MessageBox.Show($"{line1}\n{line2}", title);
            }
        }

        private static async Task ModelSaber(Uri uri)
        {
            switch (uri.Host)
            {
                case "avatar":
                    await DownloadAsset(ModelSaberURLPrefix + uri.Host + uri.AbsolutePath, CustomAvatarsFolder);
                    break;
                case "saber":
                    await DownloadAsset(ModelSaberURLPrefix + uri.Host + uri.AbsolutePath, CustomSabersFolder);
                    break;
                case "platform":
                    await DownloadAsset(ModelSaberURLPrefix + uri.Host + uri.AbsolutePath, CustomPlatformsFolder);
                    break;
            }
        }

        private static async Task DownloadAsset(string link, string folder, string fileName = null)
        {
            if (string.IsNullOrEmpty(BeatSaberPath))
            {
                Utils.SendNotify((string)Application.Current.FindResource("OneClick:InstallDirNotFound"));
            }
            try
            {
                Directory.CreateDirectory(Path.Combine(BeatSaberPath, folder));
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = WebUtility.UrlDecode(Path.Combine(BeatSaberPath, folder, new Uri(link).Segments.Last()));
                }
                else
                {
                    fileName = WebUtility.UrlDecode(Path.Combine(BeatSaberPath, folder, fileName));
                }

                await Utils.Download(link, fileName);
                Utils.SendNotify(string.Format((string)Application.Current.FindResource("OneClick:InstalledAsset"), Path.GetFileNameWithoutExtension(fileName)));
            }
            catch
            {
                Utils.SendNotify((string)Application.Current.FindResource("OneClick:AssetInstallFailed"));
            }
        }

        public static void Register(string Protocol, bool Background = false)
        {
            if (IsRegistered(Protocol) == true)
                return;
            try
            {
                if (Utils.IsAdmin)
                {
                    RegistryKey ProtocolKey = Registry.ClassesRoot.OpenSubKey(Protocol, true);
                    if (ProtocolKey == null)
                        ProtocolKey = Registry.ClassesRoot.CreateSubKey(Protocol, true);
                    RegistryKey CommandKey = ProtocolKey.CreateSubKey(@"shell\open\command", true);
                    if (CommandKey == null)
                        CommandKey = Registry.ClassesRoot.CreateSubKey(@"shell\open\command", true);

                    if (ProtocolKey.GetValue("OneClick-Provider", "").ToString() != "ModAssistant")
                    {
                        ProtocolKey.SetValue("URL Protocol", "", RegistryValueKind.String);
                        ProtocolKey.SetValue("OneClick-Provider", "ModAssistant", RegistryValueKind.String);
                        CommandKey.SetValue("", $"\"{Utils.ExePath}\" \"--install\" \"%1\"");
                    }

                    Utils.SendNotify(string.Format((string)Application.Current.FindResource("OneClick:ProtocolHandler:Registered"), Protocol));
                }
                else
                {
                    Utils.StartAsAdmin($"\"--register\" \"{Protocol}\"");
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
            }

            if (Background)
                Application.Current.Shutdown();
            else
                Pages.Options.Instance.UpdateHandlerStatus();
        }

        public static void Unregister(string Protocol, bool Background = false)
        {
            if (IsRegistered(Protocol) == false)
                return;
            try
            {
                if (Utils.IsAdmin)
                {
                    using (RegistryKey ProtocolKey = Registry.ClassesRoot.OpenSubKey(Protocol, true))
                    {
                        if (ProtocolKey != null
                            && ProtocolKey.GetValue("OneClick-Provider", "").ToString() == "ModAssistant")
                        {
                            Registry.ClassesRoot.DeleteSubKeyTree(Protocol);
                        }
                    }

                    Utils.SendNotify(string.Format((string)Application.Current.FindResource("OneClick:ProtocolHandler:Unregistered"), Protocol));
                }
                else
                {
                    Utils.StartAsAdmin($"\"--unregister\" \"{Protocol}\"");
                }

            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
            }

            if (Background)
                Application.Current.Shutdown();
            else
                Pages.Options.Instance.UpdateHandlerStatus();
        }

        public static bool IsRegistered(string Protocol)
        {
            RegistryKey ProtocolKey = Registry.ClassesRoot.OpenSubKey(Protocol);
            if (ProtocolKey != null
                && ProtocolKey.GetValue("OneClick-Provider", "").ToString() == "ModAssistant")
                return true;
            else
                return false;
        }
    }

#pragma warning disable IDE1006 // Naming Styles
    class BeatSaverApiResponse
    {
        public Metadata metadata { get; set; }
        public Stats stats { get; set; }
        public string description { get; set; }
        public DateTime? deletedAt { get; set; }
        public string _id { get; set; }
        public string key { get; set; }
        public string name { get; set; }
        public Uploader uploader { get; set; }
        public DateTime uploaded { get; set; }
        public string hash { get; set; }
        public string directDownload { get; set; }
        public string downloadURL { get; set; }
        public string coverURL { get; set; }

        public class Difficulties
        {
            public bool easy { get; set; }
            public bool normal { get; set; }
            public bool hard { get; set; }
            public bool expert { get; set; }
            public bool expertPlus { get; set; }
        }

        public class Metadata
        {
            public Difficulties difficulties { get; set; }
            public Characteristic[] characteristics { get; set; }
            public string songName { get; set; }
            public string songSubName { get; set; }
            public string songAuthorName { get; set; }
            public string levelAuthorName { get; set; }
            public double bpm { get; set; }
        }

        public class Characteristic
        {
            public string name { get; set; }
            public CharacteristicDifficulties difficulties { get; set; }
        }

        public class CharacteristicDifficulties
        {
            public Difficulty easy { get; set; }
            public Difficulty normal { get; set; }
            public Difficulty hard { get; set; }
            public Difficulty expert { get; set; }
            public Difficulty expertPlus { get; set; }
        }

        public class Difficulty
        {
            public double? duration { get; set; }
            public double? length { get; set; }
            public double bombs { get; set; }
            public double notes { get; set; }
            public double obstacles { get; set; }
            public double njs { get; set; }
        }

        public class Stats
        {
            public int downloads { get; set; }
            public int plays { get; set; }
            public int downVotes { get; set; }
            public int upVotes { get; set; }
            public double heat { get; set; }
            public double rating { get; set; }
        }

        public class Uploader
        {
            public string _id { get; set; }
            public string username { get; set; }
        }
    }
}
#pragma warning restore IDE1006 // Naming Styles
