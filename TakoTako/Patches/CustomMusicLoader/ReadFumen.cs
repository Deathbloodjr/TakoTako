using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TakoTako.Patches.CustomMusicLoader
{
    public partial class CustomMusicLoaderPatch
    {

        #region Read Fumen

        private static readonly Regex fumenFilePathRegex = new Regex("(?<songID>.*?)_(?<difficulty>[ehmnx])(_(?<songIndex>[12]))?.bin");
        private static readonly System.Collections.Generic.Dictionary<string, byte[]> pathToData = new System.Collections.Generic.Dictionary<string, byte[]>();

        [HarmonyPatch(typeof(Cryptgraphy), nameof(Cryptgraphy.ReadAllAesAndGZipBytes))]
        [HarmonyPrefix]
        private static bool ReadAllAesAndGZipBytes_Prefix(string path, Cryptgraphy.AesKeyType type,
#if TAIKO_IL2CPP
        ref Il2CppStructArray<byte> __result
#elif TAIKO_MONO
            ref byte[] __result
#endif
        )
        {
            if (pathToData.TryGetValue(path, out var data))
            {
                __result = data;
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(FumenLoader.PlayerData), nameof(FumenLoader.PlayerData.Read))]
        [HarmonyPrefix]
        private static void Read_Prefix(FumenLoader.PlayerData __instance, ref string filePath)
        {
            GetCustomSaveData().LastSongID = 0;
            if (File.Exists(filePath))
                return;

            // if the file doesn't exist, perhaps it's a custom song?
            var fileName = Path.GetFileName(filePath);
            var match = fumenFilePathRegex.Match(fileName);
            if (!match.Success)
            {
                Log.LogError($"Cannot interpret {fileName}");
                return;
            }

            // get song id
            var songId = match.Groups["songID"].Value;
            var difficulty = match.Groups["difficulty"].Value;
            var songIndex = match.Groups["songIndex"].Value;

            if (!idToSong.TryGetValue(songId, out var songInstance))
            {
                Log.LogError($"Cannot find song with id: {songId}");
                return;
            }

            GetCustomSaveData().LastSongID = songInstance.UniqueId;
            SaveCustomData();
            var path = songInstance.FolderPath;
            var songName = songInstance.SongName;

            var files = Directory.GetFiles(path, "*.bin");
            if (files.Length == 0)
            {
                Log.LogError($"Cannot find fumen at {path}");
                return;
            }

            var customPath = GetPathOfBestFumen();
            if (!File.Exists(customPath))
            {
                Log.LogError($"Cannot find fumen for {customPath}");
                return;
            }

            byte[] array = File.ReadAllBytes(customPath);
            if (songInstance.areFilesGZipped)
            {
                using var memoryStream = new MemoryStream(array);
                using var destination = new MemoryStream();
                using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
                gzipStream.CopyTo(destination);
                array = destination.ToArray();

                pathToData[customPath] = array;
            }
            else
            {
                pathToData[customPath] = array;
            }

            filePath = customPath;

            string GetPathOfBestFumen()
            {
                var baseSongPath = Path.Combine(path, $"{songName}");
                var withDifficulty = baseSongPath + $"_{difficulty}";
                var withSongIndex = withDifficulty + (string.IsNullOrWhiteSpace(songIndex) ? "" : $"_{songIndex}");

                var testPath = withSongIndex + ".bin";
                if (File.Exists(testPath))
                    return testPath;

                testPath = withDifficulty + ".bin";
                if (File.Exists(testPath))
                    return testPath;

                // add every difficulty below this one
                Difficulty difficultyEnum = (Difficulty)Enum.Parse(typeof(Difficulty), difficulty);
                int difficultyInt = (int)difficultyEnum;

                var checkDifficulties = new System.Collections.Generic.List<Difficulty>();

                for (int i = 1; i < (int)Difficulty.Count; i++)
                {
                    AddIfInRange(difficultyInt - i);
                    AddIfInRange(difficultyInt + i);

                    void AddIfInRange(int checkDifficulty)
                    {
                        if (checkDifficulty is >= 0 and < (int)Difficulty.Count)
                            checkDifficulties.Add((Difficulty)checkDifficulty);
                    }
                }

                foreach (var testDifficulty in checkDifficulties)
                {
                    withDifficulty = baseSongPath + $"_{testDifficulty.ToString()}";
                    testPath = withDifficulty + ".bin";
                    if (File.Exists(testPath))
                        return testPath;
                    testPath = withDifficulty + "_1.bin";
                    if (File.Exists(testPath))
                        return testPath;
                    testPath = withDifficulty + "_2.bin";
                    if (File.Exists(testPath))
                        return testPath;
                }

                // uh... can't find it?
                return string.Empty;
            }
        }

        private enum Difficulty
        {
            e,
            h,
            m,
            n,
            x,
            Count,
        }

        private static Difficulty[] AllDifficulties = (Difficulty[])Enum.GetValues(typeof(Difficulty));

        #endregion

    }
}
