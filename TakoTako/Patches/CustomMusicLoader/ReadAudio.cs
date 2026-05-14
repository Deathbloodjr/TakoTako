using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace TakoTako.Patches.CustomMusicLoader
{
    public partial class CustomMusicLoaderPatch
    {

        #region Read Song

        private static readonly Regex sheetNameRegex = new Regex("^song_(?<songName>.*?)$");
        private static readonly Regex songFilePathRegex = new Regex("sound\\/(?<sheetName>.*?)\\.bin$");


        [HarmonyPatch(typeof(Cryptgraphy), nameof(Cryptgraphy.ReadAllAesBytesAsyncInternal))]
        [HarmonyPrefix]
        [HarmonyWrapSafe]
        public static bool ReadAllAesBytesAsyncInternal_Prefix(string path, Cryptgraphy.AesKeyType type, Cryptgraphy.Request request)
        {
            if (File.Exists(path))
            {
                return true;
            }

            // Otherwise, custom song loading
            string songId = Path.GetFileName(path).Replace(".bin", "").Replace("song_", "");

            if (!idToSong.TryGetValue(songId, out var songInstance))
            {
                Log.LogError($"Cannot find song : {songId}");
                return true;
            }

            var newPath = Path.Combine(songInstance.FolderPath, $"song_{songId}.bin");

            var bytes = File.ReadAllBytes(newPath);
            if (songInstance.areFilesGZipped)
            {
                using var memoryStream = new MemoryStream(bytes);
                using var destination = new MemoryStream();
                using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
                gzipStream.CopyTo(destination);
                bytes = destination.ToArray();
            }

            request.Bytes = bytes;
            request.IsDone = true;
            return false;
        }

        /// <summary>
        /// Read an unencrypted song
        /// </summary>
        [HarmonyPatch(typeof(CriPlayer), "Load")]
        [HarmonyPrefix]
        private static bool Load_Prefix(ref bool __result, CriPlayer __instance)
        {
            var sheetName = __instance.CueSheetName;
            var path = Application.streamingAssetsPath + "/sound/" + sheetName + ".bin";

            if (File.Exists(path))
                return true;

            var match = sheetNameRegex.Match(sheetName);
            if (!match.Success)
            {
                Log.LogError($"Cannot interpret {sheetName}");
                return true;
            }

            var songName = match.Groups["songName"].Value;

            if (!idToSong.TryGetValue(songName, out var songInstance))
            {
                Log.LogError($"Cannot find song : {songName}");
                return true;
            }

            var newPath = Path.Combine(songInstance.FolderPath, $"{sheetName.Replace(songName, songInstance.SongName)}.bin");

            // load custom song
            __instance.IsPrepared = false;
            __instance.LoadingState = CriPlayer.LoadingStates.Loading;
            __instance.IsLoadSucceed = false;
            __instance.LoadTime = -1f;
            __instance.loadStartTime = Time.time;

            if (sheetName == "")
            {
                __instance.LoadingState = CriPlayer.LoadingStates.Finished;
                __result = false;
                return false;
            }

            var bytes = File.ReadAllBytes(newPath);
            if (songInstance.areFilesGZipped)
            {
                using var memoryStream = new MemoryStream(bytes);
                using var destination = new MemoryStream();
                using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
                gzipStream.CopyTo(destination);
                bytes = destination.ToArray();
            }

            var cueSheet = CriAtom.AddCueSheetAsync(sheetName, bytes, null);
            __instance.CueSheet = cueSheet;

            if (cueSheet != null)
            {
                __result = true;
                return false;
            }

            __instance.LoadingState = CriPlayer.LoadingStates.Finished;
            __result = false;
            return false;
        }

        #endregion

    }
}
