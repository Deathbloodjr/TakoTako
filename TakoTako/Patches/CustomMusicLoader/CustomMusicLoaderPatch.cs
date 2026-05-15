using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BepInEx.Logging;
using HarmonyLib;
#if TAIKO_IL2CPP
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Object = Il2CppSystem.Object;
#endif
using Newtonsoft.Json;
using SongSelect;
using SongSelectRanking;
using TakoTako.Common;
using UnityEngine;

namespace TakoTako.Patches.CustomMusicLoader;

/// <summary>
/// This will allow custom songs to be read in
/// </summary>
[HarmonyPatch]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public partial class CustomMusicLoaderPatch
{
    public static int SaveDataMax => DataConst.MusicMax;

    public static string PreviousMusicTrackDirectory = string.Empty;
    public static string MusicTrackDirectory => Plugin.Instance.ConfigSongDirectory.Value;
    public static string SaveFilePath => $"{Plugin.Instance.ConfigSaveDirectory.Value}/save.json";
    private const string SongDataFileName = "data.json";

    public static ManualLogSource Log => Plugin.Log;

    public static void Setup()
    {
        CreateDirectoryIfNotExist(Path.GetDirectoryName(SaveFilePath));
        CreateDirectoryIfNotExist(MusicTrackDirectory);

        void CreateDirectoryIfNotExist(string path)
        {
            path = Path.GetFullPath(path);
            if (!Directory.Exists(path))
            {
                Log.LogInfo($"Creating path at {path}");
                Directory.CreateDirectory(path);
            }
        }
    }

    public static void Reload()
    {
        Setup();
        if (Path.GetFullPath(MusicTrackDirectory) != PreviousMusicTrackDirectory)
        {
            ReloadCustomSongs();
        }
        ReloadSaveData();
    }

   
    #region Data Structures

    [Serializable]
    public class CustomMusicSaveDataBody
    {
        public int LastSongID;
        public System.Collections.Generic.Dictionary<int, MusicInfoEx> CustomTrackToMusicInfoEx = new();
        public System.Collections.Generic.Dictionary<int, EnsoRecordInfo[]> CustomTrackToEnsoRecordInfo = new();
    }

    /// <summary>
    /// This acts as a wrapper for the taiko save data formatting to decrease file size
    /// </summary>
    [Serializable]
    public class CustomMusicSaveDataBodySerializable
    {
        [JsonProperty("l")] public int LastSongID;

        [JsonProperty("m")] public System.Collections.Generic.Dictionary<int, MusicInfoExSerializable> CustomTrackToMusicInfoEx = new();

        [JsonProperty("CustomTrackToMusicInfoEx")]
        public System.Collections.Generic.Dictionary<int, MusicInfoExSerializable> CustomTrackToMusicInfoEx_v0
        {
            set => CustomTrackToMusicInfoEx = value;
        }

        [JsonProperty("r")] public System.Collections.Generic.Dictionary<int, EnsoRecordInfoSerializable[]> CustomTrackToEnsoRecordInfo = new();

        [JsonProperty("CustomTrackToEnsoRecordInfo")]
        public System.Collections.Generic.Dictionary<int, EnsoRecordInfoSerializable[]> CustomTrackToEnsoRecordInfo_v0
        {
            set => CustomTrackToEnsoRecordInfo = value;
        }

        public static explicit operator CustomMusicSaveDataBodySerializable(CustomMusicSaveDataBody m)
        {
            var result = new CustomMusicSaveDataBodySerializable();
            result.LastSongID = m.LastSongID;

            foreach (var musicInfoEx in m.CustomTrackToMusicInfoEx)
                result.CustomTrackToMusicInfoEx[musicInfoEx.Key] = musicInfoEx.Value;

            foreach (var ensoRecord in m.CustomTrackToEnsoRecordInfo)
            {
                var array = new EnsoRecordInfoSerializable[ensoRecord.Value.Length];
                for (var i = 0; i < ensoRecord.Value.Length; i++)
                    array[i] = ensoRecord.Value[i];

                result.CustomTrackToEnsoRecordInfo[ensoRecord.Key] = array;
            }

            return result;
        }

        public static explicit operator CustomMusicSaveDataBody(CustomMusicSaveDataBodySerializable m)
        {
            var result = new CustomMusicSaveDataBody();
            result.LastSongID = m.LastSongID;

            foreach (var musicInfoEx in m.CustomTrackToMusicInfoEx)
                result.CustomTrackToMusicInfoEx[musicInfoEx.Key] = musicInfoEx.Value;

            foreach (var ensoRecord in m.CustomTrackToEnsoRecordInfo)
            {
                var array = new EnsoRecordInfo[ensoRecord.Value.Length];
                for (var i = 0; i < ensoRecord.Value.Length; i++)
                    array[i] = ensoRecord.Value[i];

                result.CustomTrackToEnsoRecordInfo[ensoRecord.Key] = array;
            }

            return result;
        }

        [Serializable]
        public class MusicInfoExSerializable
        {
            [JsonProperty("f", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
            public bool favorite;

            [JsonProperty("favorite")]
            private bool favorite_v0
            {
                set => favorite = value;
            }

            [JsonProperty("n", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
            public bool isNew;

            [JsonProperty("isNew")]
            private bool isNew_v0
            {
                set => isNew = value;
            }

            public static implicit operator MusicInfoEx(MusicInfoExSerializable m) => new()
            {
                favorite = m.favorite,
                isNew = m.isNew,
            };

            public static implicit operator MusicInfoExSerializable(MusicInfoEx m) => new()
            {
                favorite = m.favorite,
                isNew = m.isNew,
            };
        }

        [Serializable]
        public class EnsoRecordInfoSerializable
        {
            [JsonProperty("h", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
            public HiScoreRecordInfoSerializable normalHiScore;

            [JsonProperty("normalHiScore", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
            private HiScoreRecordInfoSerializable normalHiScore_v0
            {
                set => normalHiScore = value;
            }

            [JsonProperty("c", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
            public DataConst.CrownType crown;

            [JsonProperty("crown", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
            private DataConst.CrownType crown_v0
            {
                set => crown = value;
            }

            [JsonProperty("p", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
            public int playCount;

            [JsonProperty("playCount", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
            private int playCount_v0
            {
                set => playCount = value;
            }

            [JsonProperty("l", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
            public bool cleared;

            [JsonProperty("cleared", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
            private bool cleared_v0
            {
                set => cleared = value;
            }

            [JsonProperty("g", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
            public bool allGood;

            [JsonProperty("allGood", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
            private bool allGood_v0
            {
                set => allGood = value;
            }

            public static implicit operator EnsoRecordInfo(EnsoRecordInfoSerializable e) => new()
            {
                normalHiScore = e.normalHiScore,
                crown = e.crown,
                playCount = e.playCount,
                cleared = e.cleared,
                allGood = e.allGood,
            };

            public static implicit operator EnsoRecordInfoSerializable(EnsoRecordInfo e) => new()
            {
                normalHiScore = e.normalHiScore,
                crown = e.crown,
                playCount = e.playCount,
                cleared = e.cleared,
                allGood = e.allGood,
            };

            [Serializable]
            public struct HiScoreRecordInfoSerializable
            {
                [JsonProperty("s", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
                public int score;

                [JsonProperty("score", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
                public int score_v0
                {
                    set => score = value;
                }

                [JsonProperty("e", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
                public short excellent;

                [JsonProperty("excellent", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
                public short excellent_v0
                {
                    set => excellent = value;
                }

                [JsonProperty("g", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
                public short good;

                [JsonProperty("good", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
                public short good_v0
                {
                    set => good = value;
                }

                [JsonProperty("b", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
                public short bad;

                [JsonProperty("bad", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
                public short bad_v0
                {
                    set => bad = value;
                }

                [JsonProperty("c", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
                public short combo;

                [JsonProperty("combo", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
                public short combo_v0
                {
                    set => combo = value;
                }

                [JsonProperty("r", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
                public short renda;

                [JsonProperty("renda", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
                public short renda_v0
                {
                    set => renda = value;
                }

                public static implicit operator HiScoreRecordInfo(HiScoreRecordInfoSerializable h) => new()
                {
                    score = h.score,
                    excellent = h.excellent,
                    good = h.good,
                    bad = h.bad,
                    combo = h.combo,
                    renda = h.renda,
                };

                public static implicit operator HiScoreRecordInfoSerializable(HiScoreRecordInfo h) => new()
                {
                    score = h.score,
                    excellent = h.excellent,
                    good = h.good,
                    bad = h.bad,
                    combo = h.combo,
                    renda = h.renda,
                };
            }
        }
    }

    #endregion

    private class ConversionStatus
    {
        public static Regex ConversionResultRegex = new("(?<ID>-?\\d*)\\:(?<PATH>.*?)$");

        [JsonProperty("i")] public System.Collections.Generic.List<ConversionItem> Items = new();

        public override string ToString()
        {
            return $"{nameof(Items)}: {string.Join(",", Items)}";
        }

        public class ConversionItem
        {
            [JsonIgnore] public const int CurrentVersion = 3;
            [JsonIgnore] public const int MaxAttempts = 3;

            [JsonProperty("f")] public string FolderName;
            [JsonProperty("a")] public int Attempts;
            [JsonProperty("s")] public bool Successful;
            [JsonProperty("v")] public int Version;
            [JsonProperty("e")] public int ResultCode;

            public override string ToString()
            {
                return $"{nameof(FolderName)}: {FolderName}, {nameof(Attempts)}: {Attempts}, {nameof(Successful)}: {Successful}, {nameof(Version)}: {Version}";
            }
        }
    }

    public class SongInstance : CustomSong
    {
        public string FolderPath;
        public string SongName;
        public int UniqueId;
    }
}
