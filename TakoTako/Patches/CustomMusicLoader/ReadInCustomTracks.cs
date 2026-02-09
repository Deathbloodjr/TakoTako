using HarmonyLib;
using PlayFab.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TakoTako.Common;
using UnityEngine;

namespace TakoTako.Patches.CustomMusicLoader
{
    public partial class CustomMusicLoaderPatch
    {

        #region Read in custom tracks

        [HarmonyPatch(typeof(DataManager), nameof(DataManager.Awake))]
        [HarmonyPostfix]
        [HarmonyWrapSafe]
        private static void DataManager_PostFix(DataManager __instance)
        {
            if (__instance.MusicData != null)
            {
                MusicDataInterface_Postfix(__instance.MusicData);
                SongDataInterface_Postfix(__instance.SongData);
            }
        }

        static string curLanguage = string.Empty;
        [HarmonyPatch(typeof(DataManager), nameof(DataManager.ExchangeWordData))]
        [HarmonyPrefix]
        [HarmonyWrapSafe]
        private static bool ExchangeWordData_Prefix(DataManager __instance, string language)
        {
            curLanguage = language;
            if (__instance.MusicData != null)
            {
                __instance.WordData = CreateWordDateInterface(language);
                return false;
            }

            return true;
        }

        public static void ReadInCustomSongs()
        {
            var dataManager = TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MyDataManager;

            if (dataManager != null &&
                dataManager.MusicData != null)
            {
                MusicDataInterface_Postfix(dataManager.MusicData);
                SongDataInterface_Postfix(dataManager.SongData);
                dataManager.WordData = CreateWordDateInterface(curLanguage);
            }
        }

        /// <summary>
        /// This will handle loading the meta data of tracks
        /// </summary>
        private static void MusicDataInterface_Postfix(MusicDataInterface __instance)
        {
            try
            {
                // This is where the metadata for tracks are read in our attempt to allow custom tracks will be to add additional metadata to the list that is created
                Log.LogInfo("Injecting custom songs");

                var customSongs = GetCustomSongs();
                if (customSongs.Count == 0)
                    return;

                // now that we have loaded this json, inject it into the existing `musicInfoAccessers`
                var musicInfoAccessors = __instance.musicInfoAccessers;

                #region Logic from the original constructor

                foreach (var song in customSongs)
                {
                    if (song == null)
                        continue;

                    var musicInfo = new MusicDataInterface.MusicInfoAccesser(
                        song.UniqueId, // From SongInstance, as we always recalculate it now
                        song.id,
                        $"song_{song.id}",
                        song.order,
                        song.genreNo,
                        true, // We always want to mark songs as DLC, otherwise ranked games will be broken as you are gonna match songs that other people don't have
                        false,
                        0,
                        true, // can we capture footage
                        2,    // Always mark custom songs as "both players need to have this song to play it"
                        new[] { song.branchEasy, song.branchNormal, song.branchHard, song.branchMania, song.branchUra },
                        new[] { song.starEasy, song.starNormal, song.starHard, song.starMania, song.starUra },
                        new[] { song.shinutiEasy, song.shinutiNormal, song.shinutiHard, song.shinutiMania, song.shinutiUra },
                        new[] { song.shinutiEasyDuet, song.shinutiNormalDuet, song.shinutiHardDuet, song.shinutiManiaDuet, song.shinutiUraDuet },
                        new[] { song.scoreEasy, song.scoreNormal, song.scoreHard, song.scoreMania, song.scoreUra }
#if TAIKO_IL2CPP
                    , 0,          // no idea what this is, going to mark them as default for now :)
                    string.Empty, // no idea what this is, going to mark them as default for now :)
                    string.Empty, // no idea what this is, going to mark them as default for now :)
                    false,        // no idea what this is, going to mark them as default for now :),
                    new[]         // no idea what this is, setting it to shinuti score
                    {
                        song.shinutiEasy, song.shinutiNormal, song.shinutiHard, song.shinutiMania, song.shinutiUra
                    }, new[] // no idea what this is, setting it to shinuti duet score
                    {
                        song.shinutiEasyDuet, song.shinutiNormalDuet, song.shinutiHardDuet, song.shinutiManiaDuet, song.shinutiUraDuet
                    }
#endif
                    );
                    musicInfoAccessors.Add(musicInfo);
                }

                #endregion

                BubbleSort(musicInfoAccessors, (a, b) => a.Order - b.Order);
                __instance.musicInfoAccessers = musicInfoAccessors;
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        }

#if TAIKO_IL2CPP
    // this is to work around sorting unmanaged lists
    public static void BubbleSort<T>(Il2CppSystem.Collections.Generic.List<T> data, Func<T, T, int> compare) where T : Object
    {
        var tempList = new System.Collections.Generic.List<T>(data.ToArray());

        BubbleSort(tempList, compare);
        data.Clear();
        foreach (var temp in tempList)
            data.Add(temp);
    }
#endif
        // this is to work around sorting unmanaged lists
        public static void BubbleSort<T>(System.Collections.Generic.List<T> data, Func<T, T, int> compare)
        {
            int i, j;
            int N = data.Count;

            for (j = N - 1; j > 0; j--)
            {
                for (i = 0; i < j; i++)
                {
                    if (compare(data[i], data[i + 1]) > 0)
                        (data[i + 1], data[i]) = (data[i], data[i + 1]);
                }
            }
        }

        /// <summary>
        /// This will handle loading the preview data of tracks
        /// </summary>
        private static void SongDataInterface_Postfix(SongDataInterface __instance)
        {
            // This is where the metadata for tracks are read in our attempt to allow custom tracks will be to add additional metadata to the list that is created
            Log.LogInfo("Injecting custom song preview data");
            var customSongs = GetCustomSongs();

            if (customSongs.Count == 0)
                return;

            // now that we have loaded this json, inject it into the existing `songInfoAccessers`
            var musicInfoAccessors = __instance.songInfoAccessers;
            if (musicInfoAccessors == null)
                return;

            foreach (var customTrack in customSongs)
            {
                if (customTrack == null)
                    continue;

                musicInfoAccessors.Add(new SongDataInterface.SongInfoAccesser(customTrack.id, customTrack.previewPos, customTrack.fumenOffsetPos));
            }

            __instance.songInfoAccessers = musicInfoAccessors;
        }

        /// <summary>
        /// This will handle loading the localisation of tracks
        /// </summary>
        private static WordDataInterface CreateWordDateInterface(string language)
        {
            var wordDataInterface = new WordDataInterface(Application.streamingAssetsPath + "/ReadAssets/newwordlist.bin", language);
            // This is where the metadata for tracks are read in our attempt to allow custom tracks will be to add additional metadata to the list that is created
            var customSongs = GetCustomSongs();

            if (customSongs.Count == 0)
                return wordDataInterface;

            var customLanguage = Plugin.Instance.ConfigOverrideDefaultSongLanguage.Value;
            var languageValue = language;
            if (customLanguage is "Japanese" or "English" or "French" or "Italian" or "German" or "Spanish" or "ChineseTraditional" or "ChineseSimplified" or "Korean")
                languageValue = customLanguage;

            // now that we have loaded this json, inject it into the existing `songInfoAccessers`
            var musicInfoAccessors = wordDataInterface.wordListInfoAccessers;

            // override the existing songs if we're using a custom language
            if (languageValue != language)
            {
                var wordListInfoRead = wordDataInterface.wordListInfoRead;
                var dictionary = wordListInfoRead.InfomationDatas.ToList();

                for (int i = 0; i < musicInfoAccessors.Count; i++)
                {
                    const string songDetailPrefix = "song_detail_";
#if TAIKO_IL2CPP
                var entry = musicInfoAccessors._items[i];
#elif TAIKO_MONO
                    var entry = musicInfoAccessors[i];
#endif
                    var index = entry.Key.IndexOf(songDetailPrefix, StringComparison.Ordinal);
                    if (index < 0)
                        continue;

                    var songTitle = entry.Key.Substring(songDetailPrefix.Length);
                    if (string.IsNullOrWhiteSpace(songTitle))
                        continue;

                    var songKey = $"song_{songTitle}";
                    var subtitleKey = $"song_sub_{songTitle}";
                    var detailKey = $"song_detail_{songTitle}";

                    var songEntry = dictionary.Find(x => x.key == songKey);
                    var subtitleEntry = dictionary.Find(x => x.key == subtitleKey);
                    var detailEntry = dictionary.Find(x => x.key == detailKey);

                    if (songEntry == null || subtitleEntry == null || detailEntry == null)
                        continue;

                    for (int j = musicInfoAccessors.Count - 1; j >= 0; j--)
                    {
#if TAIKO_IL2CPP
                        var info = musicInfoAccessors._items[j];
#elif TAIKO_MONO
                        var info = musicInfoAccessors[j];
#endif
                        if (info.Key == songKey || info.Key == subtitleKey || info.Key == detailKey)
                            musicInfoAccessors.RemoveAt(j);
                    }

                    var songValues = GetValuesWordList(songEntry);
                    var subtitleValues = GetValuesWordList(songEntry);
                    var detailValues = GetValuesWordList(songEntry);

                    musicInfoAccessors.Insert(0, new WordDataInterface.WordListInfoAccesser(songKey, songValues.text, songValues.font));
                    musicInfoAccessors.Insert(0, new WordDataInterface.WordListInfoAccesser(subtitleKey, subtitleValues.text, subtitleValues.font));
                    musicInfoAccessors.Insert(0, new WordDataInterface.WordListInfoAccesser(detailKey, detailValues.text, detailValues.font));
                }
            }

            foreach (var customTrack in customSongs)
            {
                Add($"song_{customTrack.id}", customTrack.songName);
                Add($"song_sub_{customTrack.id}", customTrack.songSubtitle);
                Add($"song_detail_{customTrack.id}", customTrack.songDetail);

                void Add(string key, TextEntry textEntry)
                {
                    var (text, font) = GetValuesTextEntry(textEntry, languageValue);
                    musicInfoAccessors.Add(new WordDataInterface.WordListInfoAccesser(key, text, font));
                }
            }

            wordDataInterface.wordListInfoAccessers = musicInfoAccessors;

            return wordDataInterface;

            (string text, int font) GetValuesWordList(WordListInfo wordListInfo)
            {
                string text;
                int font;
                switch (languageValue)
                {
                    case "Japanese":
                        text = wordListInfo.jpText;
                        font = wordListInfo.jpFontType;
                        break;
                    case "English":
                        text = wordListInfo.enText;
                        font = wordListInfo.enFontType;
                        break;
                    case "French":
                        text = wordListInfo.frText;
                        font = wordListInfo.frFontType;
                        break;
                    case "Italian":
                        text = wordListInfo.itText;
                        font = wordListInfo.itFontType;
                        break;
                    case "German":
                        text = wordListInfo.deText;
                        font = wordListInfo.deFontType;
                        break;
                    case "Spanish":
                        text = wordListInfo.esText;
                        font = wordListInfo.esFontType;
                        break;
                    case "Chinese":
                    case "ChineseT":
                    case "ChineseTraditional":
                        text = wordListInfo.tcText;
                        font = wordListInfo.tcFontType;
                        break;
                    case "ChineseSimplified":
                    case "ChineseS":
                        text = wordListInfo.scText;
                        font = wordListInfo.scFontType;
                        break;
                    case "Korean":
                        text = wordListInfo.krText;
                        font = wordListInfo.krFontType;
                        break;
                    default:
                        text = wordListInfo.enText;
                        font = wordListInfo.enFontType;
                        break;
                }

                return (text, font);
            }

            (string text, int font) GetValuesTextEntry(TextEntry textEntry, string selectedLanguage)
            {
                string text;
                int font;
                switch (selectedLanguage)
                {
                    case "Japanese":
                        text = textEntry.jpText;
                        font = textEntry.jpFont;
                        break;
                    case "English":
                        text = textEntry.enText;
                        font = textEntry.enFont;
                        break;
                    case "French":
                        text = textEntry.frText;
                        font = textEntry.frFont;
                        break;
                    case "Italian":
                        text = textEntry.itText;
                        font = textEntry.itFont;
                        break;
                    case "German":
                        text = textEntry.deText;
                        font = textEntry.deFont;
                        break;
                    case "Spanish":
                        text = textEntry.esText;
                        font = textEntry.esFont;
                        break;
                    case "Chinese":
                    case "ChineseT":
                    case "ChineseTraditional":
                        text = textEntry.tcText;
                        font = textEntry.tcFont;
                        break;
                    case "ChineseSimplified":
                    case "ChineseS":
                        text = textEntry.scText;
                        font = textEntry.scFont;
                        break;
                    case "Korean":
                        text = textEntry.krText;
                        font = textEntry.krFont;
                        break;
                    default:
                        text = textEntry.enText;
                        font = textEntry.enFont;
                        break;
                }

                // if this text is default, and we're not English / Japanese default to one of them
                if (string.IsNullOrEmpty(text) && selectedLanguage != "Japanese" && selectedLanguage != "English")
                {
                    string fallbackLanguage;
                    switch (selectedLanguage)
                    {
                        case "Chinese":
                        case "ChineseT":
                        case "ChineseTraditional":
                        case "ChineseSimplified":
                        case "ChineseS":
                        case "Korean":
                            fallbackLanguage = "Japanese";
                            break;
                        default:
                            fallbackLanguage = "English";
                            break;
                    }

                    return GetValuesTextEntry(textEntry, fallbackLanguage);
                }

                if (!string.IsNullOrEmpty(text))
                    return (text, font);

                text = textEntry.text;
                font = textEntry.font;

                return (text, font);
            }
        }

        #endregion

    }
}
