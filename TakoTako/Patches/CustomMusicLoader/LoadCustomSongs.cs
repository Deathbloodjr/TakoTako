using BepInEx.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TakoTako.Common;

namespace TakoTako.Patches.CustomMusicLoader
{
    public partial class CustomMusicLoaderPatch
    {
        #region Load Custom Songs

        private static ConcurrentBag<SongInstance> customSongsList;
        private static readonly ConcurrentDictionary<string, SongInstance> idToSong = new ConcurrentDictionary<string, SongInstance>();
        private static readonly ConcurrentDictionary<int, SongInstance> uniqueIdToSong = new ConcurrentDictionary<int, SongInstance>();

        public static void UnloadCustomSongs()
        {
            // It isn't this simple
            // We also need to remove the songs from MusicInfo
            var musicData = TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MyDataManager.MusicData;

            for (int i = 0; i < musicData.musicInfoAccessers.Count; i++)
            {
                var musicInfo = musicData.musicInfoAccessers[i];
                if (uniqueIdToSong.ContainsKey(musicInfo.UniqueId))
                {
                    idToSong.TryRemove(musicInfo.Id, out _);
                    uniqueIdToSong.TryRemove(musicInfo.UniqueId, out _);
                    musicData.musicInfoAccessers.Remove(musicInfo);
                    i--;
                    continue;
                }
            }

            customSongsList = null;
        }
        public static void ReloadCustomSongs()
        {
            UnloadCustomSongs();

            ReadInCustomSongs();

            // I guess you don't technically need to GetCustomSongs here
            // But it'd probably be better to do it here than wait for when you need it next?
            GetCustomSongs();
        }
        public static ConcurrentBag<SongInstance> GetCustomSongs()
        {
            if (customSongsList != null)
                return customSongsList;

            customSongsList = new ConcurrentBag<SongInstance>();
            if (!Directory.Exists(MusicTrackDirectory))
            {
                Log.LogError($"Cannot find {MusicTrackDirectory}");
                return customSongsList;
            }

            PreviousMusicTrackDirectory = Path.GetFullPath(MusicTrackDirectory);

            try
            {
                // add songs
                var songPaths = Directory.GetFiles(MusicTrackDirectory, "song*.bin", SearchOption.AllDirectories).Select(Path.GetDirectoryName).Distinct().ToList();
                Parallel.ForEach(songPaths, musicDirectory =>
                {
                    try
                    {
                        var directory = musicDirectory;

                        var isGenerated = musicDirectory.EndsWith("[GENERATED]");
                        if (isGenerated)
                            return;

                        SubmitDirectory(directory, false);
                    }
                    catch (Exception e)
                    {
                        Log.LogError(e);
                    }
                });
                var tjaPaths = Directory.GetFiles(MusicTrackDirectory, "*.tja", SearchOption.AllDirectories).Select(Path.GetDirectoryName).Distinct().ToList();
                // convert / add TJA songs
                Parallel.ForEach(tjaPaths, new ParallelOptions()
                {
                    MaxDegreeOfParallelism = 4
                }, musicDirectory =>
                {
                    try
                    {
                        if (IsTjaConverted(musicDirectory, out var conversionStatus) && conversionStatus != null)
                        {
                            foreach (var item in conversionStatus.Items.Where(item => item.Successful && item.Version == ConversionStatus.ConversionItem.CurrentVersion))
                                SubmitDirectory(Path.Combine(musicDirectory, item.FolderName), true);
                            return;
                        }

                        conversionStatus ??= new ConversionStatus();

                        if (conversionStatus.Items.Count > 0 && conversionStatus.Items.Any(x => !x.Successful && x.Attempts > ConversionStatus.ConversionItem.MaxAttempts))
                        {
                            Log.LogWarning($"Ignoring {musicDirectory}");
                            return;
                        }

                        try
                        {
                            var pathName = Path.GetFileName(musicDirectory);
                            var pluginDirectory = @$"{Environment.CurrentDirectory}\BepInEx\plugins\{MyPluginInfo.PLUGIN_GUID}";

                            var tjaConvertPath = @$"{pluginDirectory}\TJAConvert.exe";
                            var tja2FumenConvertPath = GetTja2FumenPath();

                            if (!File.Exists(tjaConvertPath) || string.IsNullOrWhiteSpace(tja2FumenConvertPath) || !File.Exists(tja2FumenConvertPath))
                                throw new Exception("Cannot find .exes in plugin folder");

                            Log.LogInfo($"Using {tja2FumenConvertPath} for generating TJAs");
                            Log.LogInfo($"Converting {pathName}");
                            var info = new ProcessStartInfo()
                            {
                                FileName = tjaConvertPath,
                                Arguments = $"\"{musicDirectory}\"",
                                CreateNoWindow = true,
                                WindowStyle = ProcessWindowStyle.Hidden,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                WorkingDirectory = pluginDirectory,
                                StandardOutputEncoding = Encoding.Unicode,
                            };

                            var process = new Process();
                            process.StartInfo = info;
                            process.Start();
                            var result = process.StandardOutput.ReadToEnd();
                            var resultLines = result.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                            foreach (var line in resultLines)
                            {
                                var match = ConversionStatus.ConversionResultRegex.Match(line);
                                if (!match.Success)
                                    continue;

                                var resultCode = int.Parse(match.Groups["ID"].Value);
                                var folderPath = match.Groups["PATH"].Value;

                                folderPath = Path.GetFullPath(folderPath).Replace(Path.GetFullPath(musicDirectory), ".");

                                var existingEntry = conversionStatus.Items.FirstOrDefault(x => x.FolderName == folderPath);
                                var asciiFolderPath = Regex.Replace(folderPath, @"[^\u0000-\u007F]+", string.Empty);
                                if (resultCode >= 0)
                                    Log.LogInfo($"Converted {asciiFolderPath} successfully");
                                else
                                    Log.LogError($"Could not convert {asciiFolderPath}");

                                if (existingEntry == null)
                                {
                                    conversionStatus.Items.Add(new ConversionStatus.ConversionItem()
                                    {
                                        Attempts = 1,
                                        FolderName = folderPath,
                                        Successful = resultCode >= 0,
                                        ResultCode = resultCode,
                                        Version = ConversionStatus.ConversionItem.CurrentVersion,
                                    });
                                }
                                else
                                {
                                    existingEntry.Attempts++;
                                    existingEntry.Successful = resultCode >= 0;
                                    existingEntry.ResultCode = resultCode;
                                    existingEntry.Version = ConversionStatus.ConversionItem.CurrentVersion;
                                }
                            }

                            File.WriteAllText(Path.Combine(musicDirectory, "conversion.json"), JsonConvert.SerializeObject(conversionStatus, Formatting.Indented), Encoding.Unicode);
                        }
                        catch (Exception e)
                        {
                            Log.LogError(e);
                            return;
                        }

                        if (!IsTjaConverted(musicDirectory, out conversionStatus))
                            return;

                        // if the files are converted, let's gzip those .bins to save space... because they can add up
                        foreach (var item in conversionStatus.Items)
                        {
                            var directory = Path.Combine(musicDirectory, item.FolderName);
                            var dataPath = Path.Combine(directory, SongDataFileName);
                            if (!File.Exists(dataPath))
                            {
                                Log.LogError($"Cannot find {dataPath}");
                                return;
                            }

                            var song = JsonConvert.DeserializeObject<SongInstance>(File.ReadAllText(dataPath));
                            if (song == null)
                            {
                                Log.LogError($"Cannot read {dataPath}");
                                return;
                            }

                            foreach (var filePath in Directory.EnumerateFiles(directory, "*.bin"))
                            {
                                using MemoryStream compressedMemoryStream = new MemoryStream();
                                using (FileStream originalFileStream = File.Open(filePath, FileMode.Open))
                                {
                                    using var compressor = new GZipStream(compressedMemoryStream, CompressionMode.Compress);
                                    originalFileStream.CopyTo(compressor);
                                }

                                File.WriteAllBytes(filePath, compressedMemoryStream.ToArray());
                            }

                            song.areFilesGZipped = true;
                            File.WriteAllText(dataPath, JsonConvert.SerializeObject(song, Formatting.Indented));

                            SubmitDirectory(directory, true);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.LogError(e);
                    }
                });

                if (customSongsList.Count == 0)
                    Log.LogInfo($"No tracks found");
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }

            return customSongsList;

            string GetTja2FumenPath()
            {
                // determine conversion program
                var pluginDirectory = @$"{Environment.CurrentDirectory}\BepInEx\plugins\{MyPluginInfo.PLUGIN_GUID}";
                // var tjaConvertPath = @$"{pluginDirectory}\TJAConvert.exe";
                var files = Directory
                    .EnumerateFiles(pluginDirectory)
                    .Where(x =>
                        x.Contains("tja2fumen")
                        && x.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase)).ToList();

                // if something is just called tja2fumen.exe use that
                var foundFile = files.FirstOrDefault(x => x.Contains("tja2fumen.exe"));
                if (!string.IsNullOrWhiteSpace(foundFile))
                    return foundFile;

                var regex = new Regex(@"tja2fumen\-(?<VERSION>\d?.?\d+.\d+.\d+)\.exe");
                var versionPaths = files
                    .Select(x =>
                    {
                        var match = regex.Match(x);
                        Version version = null;
                        if (match.Success)
                            Version.TryParse(match.Groups["VERSION"].Value, out version);

                        return (x, version);
                    })
                    .Where(x => x.version != null)
                    .OrderByDescending(x => x.version)
                    .FirstOrDefault();

                // try and pick the last version
                if (versionPaths.version != null)
                    return versionPaths.x;

                // just pick the first one
                if (files.Count > 0)
                    return files[0];

                var originalPath = @$"{pluginDirectory}\tja2bin.exe";
                if (File.Exists(originalPath))
                    return originalPath;

                return null;
            }

            void SubmitDirectory(string directory, bool isTjaSong)
            {
                var dataPath = Path.Combine(directory, "data.json");
                if (!File.Exists(dataPath))
                {
                    Log.LogError($"Cannot find {dataPath}");
                    return;
                }

                var song = JsonConvert.DeserializeObject<SongInstance>(File.ReadAllText(dataPath));
                if (song == null)
                {
                    Log.LogError($"Cannot read {dataPath}");
                    return;
                }

                if (Plugin.Instance.ConfigApplyGenreOverride.Value)
                {
                    // if this directory has a genre then override it
                    var fullPath = Path.GetFullPath(directory);
                    fullPath = fullPath.Replace(Path.GetFullPath(Plugin.Instance.ConfigSongDirectory.Value), "");
                    var directories = fullPath.Split('\\');
                    if (directories.Any(x => x.Equals("01 Pop", StringComparison.InvariantCultureIgnoreCase)))
                        song.genreNo = 0;
                    if (directories.Any(x => x.Equals("02 Anime", StringComparison.InvariantCultureIgnoreCase)))
                        song.genreNo = 1;
                    if (directories.Any(x => x.Equals("03 Vocaloid", StringComparison.InvariantCultureIgnoreCase)))
                        song.genreNo = 2;
                    if (directories.Any(x => x.Equals("04 Children and Folk", StringComparison.InvariantCultureIgnoreCase)))
                        song.genreNo = 4;
                    if (directories.Any(x => x.Equals("05 Variety", StringComparison.InvariantCultureIgnoreCase)))
                        song.genreNo = 3;
                    if (directories.Any(x => x.Equals("06 Classical", StringComparison.InvariantCultureIgnoreCase)))
                        song.genreNo = 5;
                    if (directories.Any(x => x.Equals("07 Game Music", StringComparison.InvariantCultureIgnoreCase)))
                        song.genreNo = 6;
                    if (directories.Any(x => x.Equals("08 Live Festival Mode", StringComparison.InvariantCultureIgnoreCase)))
                        song.genreNo = 3;
                    if (directories.Any(x => x.Equals("08 Namco Original", StringComparison.InvariantCultureIgnoreCase)))
                        song.genreNo = 7;
                }

                song.SongName = song.id;
                song.FolderPath = directory;

                // Clip off the last bit of the hash to make sure that the number is positive. This will lead to more collisions, but we should be fine.
                if (isTjaSong)
                {
                    // For TJAs, we need to hash the TJA file.
                    song.UniqueId = song.tjaFileHash;

                    if (song.UniqueId == 0)
                        throw new Exception("Converted TJA had no hash.");
                }
                else
                {
                    // For official songs, we can just use the hash of the song internal name.
                    song.UniqueId = (int)(MurmurHash2.Hash(song.id) & 0xFFFF_FFF);
                }

                if (song.UniqueId <= SaveDataMax)
                    song.UniqueId += SaveDataMax;

                if (uniqueIdToSong.ContainsKey(song.UniqueId))
                {
                    throw new Exception($"Song \"{song.id}\" has collision with \"{uniqueIdToSong[song.UniqueId].id}\", bailing out...");
                }

                song.id += $"_custom_{song.UniqueId}";
                customSongsList.Add(song);
                idToSong[song.id] = song;
                uniqueIdToSong[song.UniqueId] = song;
                // This spam doesn't need to be done on startup every time
                // We just need to know if songs failed to be added, not that all 2000+ were added
                //ModLogger.Log($"Added{(isTjaSong ? " TJA" : "")} Song {song.songName.text}({song.UniqueId})", LogType.Debug);
            }
        }

        private static bool IsTjaConverted(string directory, out ConversionStatus conversionStatus)
        {
            conversionStatus = null;
            if (!Directory.Exists(directory))
                return false;

            var conversionFile = Path.Combine(directory, "conversion.json");
            if (!File.Exists(conversionFile))
                return false;

            var json = File.ReadAllText(conversionFile, Encoding.Unicode);
            try
            {
                conversionStatus = JsonConvert.DeserializeObject<ConversionStatus>(json);
                if (conversionStatus == null)
                    return false;

                return conversionStatus.Items.Count != 0 && conversionStatus.Items.All(x => x.Successful && x.Version == ConversionStatus.ConversionItem.CurrentVersion);
            }
            catch
            {
                return false;
            }
        }

        #endregion

    }
}
