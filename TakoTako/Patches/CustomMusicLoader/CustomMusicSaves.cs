using HarmonyLib;
using Newtonsoft.Json;
using PlayFab.Internal;
using SongSelect;
using SongSelectRanking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace TakoTako.Patches.CustomMusicLoader
{
    public partial class CustomMusicLoaderPatch
    {
        #region Custom Save Data

        private static CustomMusicSaveDataBody _customSaveData;

        public static void UnloadSaveData()
        {
            _customSaveData = null;
        }

        public static void ReloadSaveData()
        {
            UnloadSaveData();
            GetCustomSaveData();
        }

        private static CustomMusicSaveDataBody GetCustomSaveData()
        {
            if (_customSaveData != null)
                return _customSaveData;

            var savePath = SaveFilePath;
            CustomMusicSaveDataBody data;
            try
            {
                if (!File.Exists(savePath))
                {
                    data = new CustomMusicSaveDataBody();
                    SaveCustomData();
                }
                else
                {
                    using var fileStream = File.OpenRead(savePath);
                    data = (CustomMusicSaveDataBody)JsonConvert.DeserializeObject<CustomMusicSaveDataBodySerializable>(File.ReadAllText(savePath));
                    data.CustomTrackToEnsoRecordInfo ??= new System.Collections.Generic.Dictionary<int, EnsoRecordInfo[]>();
                    data.CustomTrackToMusicInfoEx ??= new System.Collections.Generic.Dictionary<int, MusicInfoEx>();
                }

                _customSaveData = data;
                return data;
            }
            catch (Exception e)
            {
                ModLogger.Log($"Could not load custom data, creating a fresh one\n {e}", LogType.Error);
            }

            data = new CustomMusicSaveDataBody();
            SaveCustomData();
            return data;
        }

        private static int saveMutex = 0;

        private static void SaveCustomData()
        {
            if (!Plugin.Instance.ConfigSaveEnabled.Value)
                return;

            if (_customSaveData == null)
                return;

            saveMutex++;
            if (saveMutex > 1)
                return;

            SaveData();

            async void SaveData()
            {
                while (saveMutex > 0)
                {
                    saveMutex = 0;
                    ModLogger.Log("Saving custom data");
                    try
                    {
                        var data = GetCustomSaveData();
                        var savePath = SaveFilePath;
                        var json = JsonConvert.SerializeObject((CustomMusicSaveDataBodySerializable)data);

                        using Stream fs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
                        using var streamWriter = new StreamWriter(fs);
                        await streamWriter.WriteAsync(json);
                    }
                    catch (Exception e)
                    {
                        ModLogger.Log($"Could not save custom data \n {e}", LogType.Error);
                    }
                }
            }
        }

        #endregion


        #region Loading / Save Custom Save Data

        /// <summary>
        /// When loading, make sure to ignore custom tracks, as their IDs will be different
        /// </summary>
        [HarmonyPatch(typeof(SongSelectManager), nameof(SongSelectManager.LoadSongList))]
        [HarmonyPrefix]
        private static bool LoadSongList_Prefix(SongSelectManager __instance)
        {
            #region Edited Code

            Log.LogInfo("Loading custom save");
            var customData = GetCustomSaveData();

            #endregion

            #region Setup instanced variables / methods

            var playDataMgr = __instance.playDataMgr;
            var musicInfoAccess = __instance.musicInfoAccess;
            var enableKakuninSong = __instance.enableKakuninSong;
            var getLocalizedText = (string x) => __instance.GetLocalizedText(x);
            var updateSortCategoryInfo = __instance.UpdateSortCategoryInfo;

            #endregion

            if (playDataMgr == null)
            {
                Log.LogError("Could not find playDataMgr");
                return true;
            }

            var unsortedSongList = __instance.UnsortedSongList;
            unsortedSongList.Clear();
#if TAIKO_IL2CPP
        playDataMgr.GetMusicInfoExAllIl2cpp(0, out var dst);
#elif TAIKO_MONO
            playDataMgr.GetMusicInfoExAll(0, out var dst);
#endif
            for (int i = 0; i < 8; i++)
            {
                for (int j = 0; j < musicInfoAccess.Length; j++)
                {

                    if (musicInfoAccess[j].GenreNo != i)
                    {
                        continue;
                    }

                    if (!enableKakuninSong && musicInfoAccess[j].IsKakuninSong())
                    {
                        continue;
                    }

                    if (musicInfoAccess[j].Price != 0)
                    {
                        playDataMgr.GetUnlockInfo(0, DataConst.ItemType.Music, musicInfoAccess[j].UniqueId, out var dst3);
                        if (!dst3.isUnlock && musicInfoAccess[j].Price != 0)
                        {
                            continue;
                        }
                    }



                    SongSelectManager.Song song2 = new SongSelectManager.Song();
                    song2.PreviewIndex = j;
                    song2.Id = musicInfoAccess[j].Id;
                    song2.TitleKey = "song_" + musicInfoAccess[j].Id;
                    song2.SubKey = "song_sub_" + musicInfoAccess[j].Id;
                    song2.RubyKey = "song_detail_" + musicInfoAccess[j].Id;
                    song2.UniqueId = musicInfoAccess[j].UniqueId;
                    song2.SongGenre = musicInfoAccess[j].GenreNo;
                    song2.ListGenre = i;
                    song2.Order = musicInfoAccess[j].Order;
                    song2.TitleText = getLocalizedText("song_" + song2.Id);
                    song2.SubText = getLocalizedText("song_sub_" + song2.Id);
                    song2.DetailText = getLocalizedText("song_detail_" + song2.Id);
                    song2.Stars = musicInfoAccess[j].Stars;
                    song2.Branches = musicInfoAccess[j].Branches;
                    song2.HighScores = new SongSelectManager.Score[5];
                    song2.HighScores2P = new SongSelectManager.Score[5];
                    song2.DLC = musicInfoAccess[j].IsDLC;
                    song2.Price = musicInfoAccess[j].Price;
                    song2.IsCap = true; // should DVR Capture be enabled?
                    if (TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MyDataManager.SongData.GetInfo(song2.Id) != null)
                    {
                        song2.AudioStartMS = TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MyDataManager.SongData.GetInfo(song2.Id).PreviewPos;
                    }
                    else
                    {
                        song2.AudioStartMS = 0;
                    }

                    if (dst != null)
                    {
                        #region Edited Code

                        MusicInfoEx data;
                        if (uniqueIdToSong.ContainsKey(musicInfoAccess[j].UniqueId))
                        {
                            customData.CustomTrackToMusicInfoEx.TryGetValue(musicInfoAccess[j].UniqueId, out var objectData);
                            data = objectData;
                        }
                        else
                            data = dst[musicInfoAccess[j].UniqueId];

                        song2.Favorite = data.favorite;
                        song2.NotPlayed = new bool[5];
                        song2.NotCleared = new bool[5];
                        song2.NotFullCombo = new bool[5];
                        song2.NotDondaFullCombo = new bool[5];
                        song2.NotPlayed2P = new bool[5];
                        song2.NotCleared2P = new bool[5];
                        song2.NotFullCombo2P = new bool[5];
                        song2.NotDondaFullCombo2P = new bool[5];
                        bool isNew = data.isNew;

                        #endregion

                        for (int k = 0; k < 5; k++)
                        {
                            GetPlayerRecordInfo(playDataMgr, 0, musicInfoAccess[j].UniqueId, (EnsoData.EnsoLevelType)k, out var dst4);
                            song2.NotPlayed[k] = dst4.playCount <= 0;
                            song2.NotCleared[k] = dst4.crown < DataConst.CrownType.Silver;
                            song2.NotFullCombo[k] = dst4.crown < DataConst.CrownType.Gold;
                            song2.NotDondaFullCombo[k] = dst4.crown < DataConst.CrownType.Rainbow;
#if TAIKO_IL2CPP
                        var highScore1 = song2.HighScores[k];
                        highScore1.hiScoreRecordInfos = dst4.normalHiScore;
                        highScore1.crown = dst4.crown;
                        song2.HighScores[k] = highScore1;
#elif TAIKO_MONO
                            song2.HighScores[k].hiScoreRecordInfos = dst4.normalHiScore;
                            song2.HighScores[k].crown = dst4.crown;
#endif
                            GetPlayerRecordInfo(playDataMgr, 1, musicInfoAccess[j].UniqueId, (EnsoData.EnsoLevelType)k, out var dst5);
                            song2.NotPlayed2P[k] = dst5.playCount <= 0;
                            song2.NotCleared2P[k] = dst4.crown < DataConst.CrownType.Silver;
                            song2.NotFullCombo2P[k] = dst5.crown < DataConst.CrownType.Gold;
                            song2.NotDondaFullCombo2P[k] = dst5.crown < DataConst.CrownType.Rainbow;

#if TAIKO_IL2CPP
                        var highScore2 = song2.HighScores2P[k];
                        highScore2.hiScoreRecordInfos = dst5.normalHiScore;
                        highScore2.crown = dst5.crown;
                        song2.HighScores2P[k] = highScore2;
#elif TAIKO_MONO
                            song2.HighScores2P[k].hiScoreRecordInfos = dst5.normalHiScore;
                            song2.HighScores2P[k].crown = dst5.crown;
#endif
                        }

                        song2.NewSong = isNew && (song2.DLC || song2.Price > 0);
                    }

                    unsortedSongList.Add(song2);
                }
            }

            BubbleSort(unsortedSongList, (a, b) =>
            {
                var value = a.SongGenre.CompareTo(b.SongGenre);
                if (value != 0)
                    return value;

                return a.Order - b.Order;
            });

            __instance.SongList.Clear();
            foreach (var song in unsortedSongList)
                __instance.SongList.Add(song);

            __instance.UnsortedSongList = unsortedSongList;

            updateSortCategoryInfo(DataConst.SongSortType.Genre);
            return false;
        }

        /// <summary>
        /// When saving favourite tracks, save the custom ones too
        /// </summary>
        [HarmonyPatch(typeof(SongSelectManager), "SaveFavotiteSongs")]
        [HarmonyPrefix]
        private static bool SaveFavotiteSongs_Prefix(SongSelectManager __instance)
        {
#if TAIKO_IL2CPP
        __instance.playDataMgr.GetMusicInfoExAllIl2cpp(0, out var dst);
#elif TAIKO_MONO
            __instance.playDataMgr.GetMusicInfoExAll(0, out var dst);
#endif
            var customSaveData = GetCustomSaveData();

            bool saveCustomData = false;
            int num = 0;
            foreach (var unsortedSong in __instance.UnsortedSongList)
            {
                num++;
                if (uniqueIdToSong.ContainsKey(unsortedSong.UniqueId))
                {
                    customSaveData.CustomTrackToMusicInfoEx.TryGetValue(unsortedSong.UniqueId, out var data);
                    saveCustomData |= data.favorite != unsortedSong.Favorite;
                    data.favorite = unsortedSong.Favorite;
                    customSaveData.CustomTrackToMusicInfoEx[unsortedSong.UniqueId] = data;
                }
                else
                {
                    var entry = dst[unsortedSong.UniqueId];
                    entry.favorite = unsortedSong.Favorite;
                    dst[unsortedSong.UniqueId] = entry;

                    __instance.playDataMgr.SetMusicInfoEx(0, unsortedSong.UniqueId, ref entry, num >= __instance.UnsortedSongList.Count);
                }
            }

            if (saveCustomData)
                SaveCustomData();

            return false;
        }

        /// <summary>
        /// When loading the song, mark the custom song as not new
        /// </summary>
        [HarmonyPatch(typeof(CourseSelect), "EnsoConfigSubmit")]
        [HarmonyPrefix]
        private static bool EnsoConfigSubmit_Prefix(CourseSelect __instance)
        {
            var settings = __instance.settings;
            var playDataManager = __instance.playDataManager;
            var ensoDataManager = __instance.ensoDataManager;

            var selectedSongInfo = __instance.selectedSongInfo;
            var ensoMode = __instance.ensoMode;
            var ensoMode2P = __instance.ensoMode2P;
            var selectedCourse = __instance.selectedCourse;
            var selectedCourse2P = __instance.selectedCourse2P;
            var status = __instance.status;

            var songUniqueId = selectedSongInfo.UniqueId;

            settings.ensoType = EnsoData.EnsoType.Normal;
            settings.rankMatchType = EnsoData.RankMatchType.None;
            settings.musicuid = selectedSongInfo.Id;
            settings.musicUniqueId = songUniqueId;
            settings.genre = (EnsoData.SongGenre)selectedSongInfo.SongGenre;
            settings.playerNum = 1;
            var player1Entry = settings.ensoPlayerSettings[0];
            player1Entry.neiroId = ensoMode.neiro;
            player1Entry.courseType = (EnsoData.EnsoLevelType)selectedCourse;
            player1Entry.speed = ensoMode.speed;
            player1Entry.dron = ensoMode.dron;
            player1Entry.reverse = ensoMode.reverse;
            player1Entry.randomlv = ensoMode.randomlv;
            player1Entry.special = ensoMode.special;

            var array = selectedSongInfo.HighScores;
            player1Entry.hiScore = array[selectedCourse].hiScoreRecordInfos.score;
            settings.ensoPlayerSettings[0] = player1Entry;

            __instance.settings = settings;
            if (status.Is2PActive)
            {
                var player2Entry = settings.ensoPlayerSettings[1];
                player2Entry.neiroId = ensoMode2P.neiro;
                player2Entry.courseType = (EnsoData.EnsoLevelType)selectedCourse2P;
                player2Entry.speed = ensoMode2P.speed;
                player2Entry.dron = ensoMode2P.dron;
                player2Entry.reverse = ensoMode2P.reverse;
                player2Entry.randomlv = ensoMode2P.randomlv;
                player2Entry.special = ensoMode2P.special;
                GetPlayerRecordInfo(TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MyDataManager.PlayData, 1, songUniqueId, (EnsoData.EnsoLevelType)selectedCourse2P, out var dst);
                player2Entry.hiScore = dst.normalHiScore.score;
                settings.playerNum = 2;
                settings.ensoPlayerSettings[1] = player2Entry;
            }

            settings.debugSettings.isTestMenu = false;
            settings.rankMatchType = EnsoData.RankMatchType.None;
            settings.isRandomSelect = selectedSongInfo.IsRandomSelect;
            settings.isDailyBonus = selectedSongInfo.IsDailyBonus;
            ensoMode.songUniqueId = settings.musicUniqueId;
            ensoMode.level = (EnsoData.EnsoLevelType)selectedCourse;

            __instance.settings = settings;
            __instance.ensoMode = ensoMode;
            __instance.SetSaveDataEnsoMode(CourseSelect.PlayerType.Player1);
            ensoMode2P.songUniqueId = settings.musicUniqueId;
            ensoMode2P.level = (EnsoData.EnsoLevelType)selectedCourse2P;
            __instance.ensoMode2P = ensoMode2P;
            __instance.SetSaveDataEnsoMode(CourseSelect.PlayerType.Player2);

#if TAIKO_IL2CPP
        playDataManager.GetSystemOptionRemake(out var dst2);
#elif TAIKO_MONO
            playDataManager.GetSystemOption(out var dst2);
#endif

            int deviceTypeIndex = EnsoDataManager.GetDeviceTypeIndex(settings.ensoPlayerSettings[0].inputDevice);
            settings.noteDispOffset = dst2.onpuDispLevels[deviceTypeIndex];
            settings.noteDelay = dst2.onpuHitLevels[deviceTypeIndex];
            settings.songVolume = TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MySoundManager.GetVolume(SoundManager.SoundType.InGameSong);
            settings.seVolume = TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MySoundManager.GetVolume(SoundManager.SoundType.Se);
            settings.voiceVolume = TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MySoundManager.GetVolume(SoundManager.SoundType.Voice);
            settings.bgmVolume = TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MySoundManager.GetVolume(SoundManager.SoundType.Bgm);
            settings.neiroVolume = TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MySoundManager.GetVolume(SoundManager.SoundType.InGameNeiro);
            settings.effectLevel = (EnsoData.EffectLevel)dst2.qualityLevel;
            __instance.settings = settings;
#if TAIKO_IL2CPP
        ensoDataManager.SetSettingsRemake(ref settings);
#elif TAIKO_MONO
            ensoDataManager.SetSettings(ref settings);
#endif
            ensoDataManager.DecideSetting();
            if (status.Is2PActive)
            {
                dst2.controlType[1] = dst2.controlType[0];
                playDataManager.SetSystemOption(ref dst2);
            }

            var customSaveData = GetCustomSaveData();

            if (uniqueIdToSong.ContainsKey(songUniqueId))
            {
                customSaveData.CustomTrackToMusicInfoEx.TryGetValue(songUniqueId, out var data);
                data.isNew = false;
                customSaveData.CustomTrackToMusicInfoEx[songUniqueId] = data;
                SaveCustomData();
            }
            else
            {
#if TAIKO_IL2CPP
            playDataManager.GetMusicInfoExAllIl2cpp(0, out var dst3);
#elif TAIKO_MONO
                playDataManager.GetMusicInfoExAll(0, out var dst3);
#endif
                var entry = dst3[songUniqueId];
                entry.isNew = false;
                dst3[songUniqueId] = entry;

                playDataManager.SetMusicInfoEx(0, songUniqueId, ref entry);
            }

            return false;
        }

        /// <summary>
        /// When loading the song obtain isfavourite correctly
        /// </summary>
        [HarmonyPatch(typeof(KpiListCommon.MusicKpiInfo), "GetEnsoSettings")]
        [HarmonyPrefix]
        private static bool GetEnsoSettings_Prefix(KpiListCommon.MusicKpiInfo __instance)
        {
#if TAIKO_IL2CPP
        TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MyDataManager.EnsoData.CopySettingsRemake(out var dst);
#elif TAIKO_MONO
            TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MyDataManager.EnsoData.CopySettings(out var dst);
#endif
            __instance.music_id = dst.musicuid;
            __instance.genre = (int)dst.genre;
            __instance.course_type = (int)dst.ensoPlayerSettings[0].courseType;
            __instance.neiro_id = dst.ensoPlayerSettings[0].neiroId;
            __instance.speed = (int)dst.ensoPlayerSettings[0].speed;
            __instance.dron = (int)dst.ensoPlayerSettings[0].dron;
            __instance.reverse = (int)dst.ensoPlayerSettings[0].reverse;
            __instance.randomlv = (int)dst.ensoPlayerSettings[0].randomlv;
            __instance.special = (int)dst.ensoPlayerSettings[0].special;
            PlayDataManager playData = TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MyDataManager.PlayData;
            playData.GetEnsoMode(out var dst2);
            __instance.sort_course = (int)dst2.songSortCourse;
            __instance.sort_type = (int)dst2.songSortType;
            __instance.sort_filter = (int)dst2.songFilterType;
            __instance.sort_favorite = (int)dst2.songFilterTypeFavorite;
            MusicDataInterface.MusicInfoAccesser[] array = TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MyDataManager.MusicData.musicInfoAccessers.ToArray();
#if TAIKO_IL2CPP
        playData.GetMusicInfoExAllIl2cpp(0, out var dst3);
#elif TAIKO_MONO
            playData.GetMusicInfoExAll(0, out var dst3);
#endif

            #region edited code

            for (int i = 0; i < array.Length; i++)
            {
                var id = array[i].UniqueId;
                if (id == dst.musicUniqueId && dst3 != null)
                {
                    if (uniqueIdToSong.ContainsKey(id))
                    {
                        GetCustomSaveData().CustomTrackToMusicInfoEx.TryGetValue(id, out var data);
                        __instance.is_favorite = data.favorite;
                    }
                    else
                    {
                        __instance.is_favorite = dst3[id].favorite;
                    }
                }
            }

            #endregion

#if TAIKO_IL2CPP
        playData.GetPlayerInfoRemake(0, out var dst4);
#elif TAIKO_MONO
            playData.GetPlayerInfo(0, out var dst4);
#endif
            __instance.current_coins_num = dst4.donCoin;
            __instance.total_coins_num = dst4.getCoinsInTotal;
#if TAIKO_IL2CPP
        playData.GetRankMatchSeasonRecordInfoRemake(0, 0, out var dst5);
#elif TAIKO_MONO
            playData.GetRankMatchSeasonRecordInfo(0, 0, out var dst5);
#endif
            __instance.rank_point = dst5.rankPointMax;

            return false;
        }

        // This breaks GetUnlockInfo for Mono, idk about IL2CPP but I'd assume there as well?
        [HarmonyPatch(typeof(PlayDataManager), nameof(PlayDataManager.IsValueInRange))]
        [HarmonyPostfix]
        [HarmonyWrapSafe]
        private static void IsValueInRange(int myValue, int minValue, int maxValue, ref bool __result)
        {
            // if the max value is the same as music max, hopefully we're validating song ids
            // in which case return true if this is one of our songs
            if (maxValue != DataConst.MusicMax) return;

            if (uniqueIdToSong.ContainsKey(myValue))
                __result = true;
        }

#if TAIKO_MONO
        [HarmonyPatch(typeof(PlayDataManager))]
        [HarmonyPatch(nameof(PlayDataManager.GetUnlockInfo))]
        [HarmonyPrefix]
        [HarmonyWrapSafe]
        private static bool PlayDataManager_GetUnlockInfo_Prefix(PlayDataManager __instance, int playerId, DataConst.ItemType itemType, int uniqueId, out UnlockInfo dst)
        {
            // If it isn't music
            // Or if it is music and is not a custom song
            if (itemType != DataConst.ItemType.Music ||
                (itemType == DataConst.ItemType.Music &&
                !uniqueIdToSong.ContainsKey(uniqueId)))
            {
                dst = new UnlockInfo();
                return true;
            }

            dst = new UnlockInfo();
            dst.Reset();
            return false;
        }

        // Play Count currently isn't stored in the custom save file
        [HarmonyPatch(typeof(PlayDataManager))]
        [HarmonyPatch(nameof(PlayDataManager.GetPlayerRecordInfoPlayCount))]
        [HarmonyPrefix]
        [HarmonyWrapSafe]
        private static bool PlayDataManager_GetPlayerRecordInfoPlayCount_Prefix(PlayDataManager __instance, ref int __result, int playerId, int uniqueId, EnsoData.EnsoLevelType levelType)
        {
            // If it isn't music
            // Or if it is music and is not a custom song
            if (!uniqueIdToSong.ContainsKey(uniqueId))
            {
                return true;
            }

            __result = 1;
            return false;
        }

        // Play Count currently isn't stored in the custom save file
        [HarmonyPatch(typeof(PlayDataManager))]
        [HarmonyPatch(nameof(PlayDataManager.SetPlayerInfoPlayCount))]
        [HarmonyPrefix]
        [HarmonyWrapSafe]
        private static bool PlayDataManager_SetPlayerInfoPlayCount_Prefix(PlayDataManager __instance, ref bool __result, int playerId, int uniqueId, EnsoData.EnsoLevelType levelType, int countnum)
        {
            // If it isn't music
            // Or if it is music and is not a custom song
            if (!uniqueIdToSong.ContainsKey(uniqueId))
            {
                return true;
            }

            __result = true;
            return false;
        }
#endif

        #region Methods with GetPlayerRecordInfo

        // this doesn't patch well, so I have to redo each method that uses it
        // We can still patch it for Mono though, and a lot of mods use this function
        // So this means the function is unusable for any mods in IL2CPP until we can patch it properly there
        // /// <summary>
        // /// Load scores from custom save data
        // /// </summary>
#if TAIKO_MONO
        [HarmonyPatch(typeof(PlayDataManager), nameof(PlayDataManager.GetPlayerRecordInfo))]
        [HarmonyPrefix]
#endif
        public static bool GetPlayerRecordInfo(PlayDataManager __instance,
            int playerId,
            int uniqueId,
            EnsoData.EnsoLevelType levelType,
            out EnsoRecordInfo dst)
        {
            if (!uniqueIdToSong.ContainsKey(uniqueId))
            {
#if TAIKO_MONO
                dst = new EnsoRecordInfo();
#elif TAIKO_IL2CPP
            __instance.GetPlayerRecordInfo(playerId, uniqueId, levelType, out dst);
#endif
                return true;
            }

            int num = (int)levelType;
            if (num is < 0 or >= 5)
                num = 0;

            // load our custom save, this will combine the scores of player1 and player2
            var saveData = GetCustomSaveData().CustomTrackToEnsoRecordInfo;
            if (!saveData.TryGetValue(uniqueId, out var ensoData))
            {
                ensoData = new EnsoRecordInfo[(int)EnsoData.EnsoLevelType.Num];
                saveData[uniqueId] = ensoData;
            }

            dst = ensoData[num];
            return false;
        }

        [HarmonyPatch(typeof(CourseSelect), nameof(CourseSelect.SetInfo))]
        [HarmonyPostfix]
        [HarmonyWrapSafe]
        public static void SetInfo_Postfix(
            CourseSelect __instance,
            MusicDataInterface.MusicInfoAccesser song,
            bool isRandomSelect,
            bool isDailyBonus)
        {
            for (int levelType = 0; levelType < __instance.selectedSongInfo.HighScores.Length; ++levelType)
            {
                EnsoRecordInfo dst;
                GetPlayerRecordInfo(__instance.playDataManager, 0, song.UniqueId, (EnsoData.EnsoLevelType)levelType, out dst);
                var highScore = __instance.selectedSongInfo.HighScores[levelType];
                highScore.hiScoreRecordInfos = dst.normalHiScore;
                highScore.crown = dst.crown;
                __instance.selectedSongInfo.HighScores[levelType] = highScore;
            }
        }

        [HarmonyPatch(typeof(CourseSelect), nameof(CourseSelect.UpdateDiffCourseAnim))]
        [HarmonyPostfix]
        [HarmonyWrapSafe]
        public static void UpdateDiffCourseAnim_Postfix(CourseSelect __instance)
        {
            int num = __instance.selectedSongInfo.Stars[4] == 0 ? 4 : 5;
            for (int levelType = 0; levelType < num; ++levelType)
            {
                Animator iconCrown2 = __instance.diffCourseAnims[levelType].IconCrowns[1];
                if (__instance.status.Is2PActive)
                {
                    GetPlayerRecordInfo(TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MyDataManager.PlayData, 1, __instance.selectedSongInfo.UniqueId,
                        (EnsoData.EnsoLevelType)levelType, out var dst);
                    switch (dst.crown)
                    {
                        case DataConst.CrownType.Silver:
                            iconCrown2.Play("Silver");
                            break;
                        case DataConst.CrownType.Gold:
                            iconCrown2.Play("Gold");
                            break;
                        case DataConst.CrownType.Rainbow:
                            iconCrown2.Play("Rainbow");
                            break;
                        default:
                            iconCrown2.Play("None");
                            break;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(SongSelectRankingBestScoreDisplay), nameof(SongSelectRankingBestScoreDisplay.SetMyInfo))]
        [HarmonyPostfix]
        [HarmonyWrapSafe]
        public static void SetMyInfo_Postfix(SongSelectRankingBestScoreDisplay __instance, int musicUniqueId, EnsoData.EnsoLevelType ensoLevel)
        {
            GetPlayerRecordInfo(TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MyDataManager.PlayData, 0, musicUniqueId, ensoLevel, out var dst);
            __instance.UpdateScoreDisplay(dst.normalHiScore);
        }

        [HarmonyPatch(typeof(CourseSelectScoreDisplay), nameof(CourseSelectScoreDisplay.UpdateDisplay))]
        [HarmonyPostfix]
        [HarmonyWrapSafe]
        public static void UpdateDisplay_Postfix(CourseSelectScoreDisplay __instance, int musicUniqueId, EnsoData.EnsoLevelType levelType)
        {
            GetPlayerRecordInfo(TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MyDataManager.PlayData, __instance.playerType == DataConst.PlayerType.Player_1 ? 0 : 1,
                musicUniqueId, levelType, out var dst);
            var normalHiScore = dst.normalHiScore;
            for (int index = 0; index < 6; ++index)
            {
                int num = 0;
                switch (index)
                {
                    case 0:
                        num = normalHiScore.score;
                        break;
                    case 1:
                        num = (int)normalHiScore.excellent;
                        break;
                    case 2:
                        num = (int)normalHiScore.good;
                        break;
                    case 3:
                        num = (int)normalHiScore.bad;
                        break;
                    case 4:
                        num = (int)normalHiScore.combo;
                        break;
                    case 5:
                        num = (int)normalHiScore.renda;
                        break;
                }

                __instance.numDisplays[index].NumberPlayer.SetValue(num);
            }
        }

        [HarmonyPatch(typeof(SongSelectScoreDisplay), nameof(SongSelectScoreDisplay.UpdateCrownNumDisplay))]
        [HarmonyPostfix]
        [HarmonyWrapSafe]
        public static void UpdateCrownNumDisplay_Postfix(SongSelectScoreDisplay __instance, int playerId)
        {
            PlayDataManager playData = TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MyDataManager.PlayData;
            var musicInfoAccessers = TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MyDataManager.MusicData.musicInfoAccessers;
            int[,] numArray = new int[3, 5];
            foreach (MusicDataInterface.MusicInfoAccesser musicInfoAccesser in musicInfoAccessers)
            {
                int num = musicInfoAccesser.Stars[4] > 0 ? 5 : 4;
                for (int levelType = 0; levelType < num; ++levelType)
                {
                    GetPlayerRecordInfo(playData, playerId, musicInfoAccesser.UniqueId, (EnsoData.EnsoLevelType)levelType, out var dst);
                    switch (dst.crown)
                    {
                        case DataConst.CrownType.Silver:
                            ++numArray[0, levelType];
                            break;
                        case DataConst.CrownType.Gold:
                            ++numArray[1, levelType];
                            break;
                        case DataConst.CrownType.Rainbow:
                            ++numArray[2, levelType];
                            break;
                    }
                }
            }

            for (int index = 0; index < 5; ++index)
            {
                __instance.crownNums[index].CrownNumbers[0].SetNum(numArray[0, index]);
                __instance.crownNums[index].CrownNumbers[1].SetNum(numArray[1, index]);
                __instance.crownNums[index].CrownNumbers[2].SetNum(numArray[2, index]);
            }
        }

        [HarmonyPatch(typeof(SongSelectScoreDisplay), nameof(SongSelectScoreDisplay.UpdateScoreDisplay))]
        [HarmonyPostfix]
        [HarmonyWrapSafe]
        public static void UpdateScoreDisplay_Postfix(SongSelectScoreDisplay __instance, int playerId, int musicUniqueId, bool enableUra)
        {
            var num = enableUra ? 5 : 4;

            for (int levelType = 0; levelType < num; ++levelType)
            {
                GetPlayerRecordInfo(TaikoSingletonMonoBehaviour<CommonObjects>.Instance.MyDataManager.PlayData, playerId, musicUniqueId, (EnsoData.EnsoLevelType)levelType,
                    out var dst);
                __instance.bestScores[levelType].RootObject.SetValue(dst.normalHiScore.score);
            }
        }

        #endregion

        /// <summary>
        /// Save scores to custom save data
        /// </summary>
        [HarmonyPatch(typeof(PlayDataManager), "UpdatePlayerScoreRecordInfo",
            new Type[]
            {
            typeof(int), typeof(int), typeof(int), typeof(EnsoData.EnsoLevelType), typeof(bool), typeof(DataConst.SpecialTypes), typeof(HiScoreRecordInfo),
            typeof(DataConst.ResultType), typeof(bool), typeof(DataConst.CrownType)
            })]
        [HarmonyPrefix]
        public static bool UpdatePlayerScoreRecordInfo(PlayDataManager __instance, int playerId, int charaIndex, int uniqueId, EnsoData.EnsoLevelType levelType, bool isSinuchi,
            DataConst.SpecialTypes spTypes, HiScoreRecordInfo record,
            DataConst.ResultType resultType, bool savemode, DataConst.CrownType crownType)
        {
            if (!uniqueIdToSong.ContainsKey(uniqueId))
                return true;

            var saveData = GetCustomSaveData().CustomTrackToEnsoRecordInfo;
            if (!saveData.TryGetValue(uniqueId, out var ensoData))
            {
                ensoData = new EnsoRecordInfo[(int)EnsoData.EnsoLevelType.Num];
                saveData[uniqueId] = ensoData;
            }

            EnsoRecordInfo ensoRecordInfo = ensoData[(int)levelType];
#pragma warning disable Harmony003
            if (ensoRecordInfo.normalHiScore.score <= record.score)
            {
                ensoRecordInfo.normalHiScore.score = record.score;
                ensoRecordInfo.normalHiScore.combo = record.combo;
                ensoRecordInfo.normalHiScore.excellent = record.excellent;
                ensoRecordInfo.normalHiScore.good = record.good;
                ensoRecordInfo.normalHiScore.bad = record.bad;
                ensoRecordInfo.normalHiScore.renda = record.renda;
            }
#pragma warning restore Harmony003

            if (crownType != DataConst.CrownType.Off)
            {
                if (IsValueInRange((int)crownType, 0, 5) && ensoRecordInfo.crown <= crownType)
                {
                    ensoRecordInfo.crown = crownType;
                    ensoRecordInfo.cleared = crownType >= DataConst.CrownType.Silver;
                }
            }

            ensoData[(int)levelType] = ensoRecordInfo;

            if (savemode && playerId == 0)
                SaveCustomData();

            return false;

            bool IsValueInRange(int myValue, int minValue, int maxValue)
            {
                if (myValue >= minValue && myValue < maxValue)
                    return true;
                return false;
            }
        }

        [HarmonyPatch(typeof(SongSelectManager), nameof(SongSelectManager.Start))]
        [HarmonyPostfix]
        [HarmonyWrapSafe]
        public static void Start_Postfix(SongSelectManager __instance)
        {
            if (__instance.SongList == null)
                return;

            Plugin.Instance.StartCustomCoroutine(SetSelectedSongAsync());

            IEnumerator SetSelectedSongAsync()
            {
                yield return null;
                while (__instance.SongList.Count == 0 || __instance.isAsyncLoading)
                    yield return null;

                // if the song id is < 0 then fix the selected song index
                var lastPlaySongId = GetCustomSaveData().LastSongID;
                if (lastPlaySongId == 0)
                    yield break;

                var songIndex = -1;

                for (int i = 0; i < __instance.SongList.Count; i++)
                {
#if TAIKO_IL2CPP
                var song = (SongSelectManager.Song)__instance.SongList[(Index)i];
#elif TAIKO_MONO
                    var song = __instance.SongList[i];
#endif
                    if (song.UniqueId != lastPlaySongId)
                        continue;

                    songIndex = i;
                }

                if (songIndex < 0)
                    yield break;

                __instance.SelectedSongIndex = songIndex;
                __instance.songPlayer.Stop(true);
                __instance.songPlayer.Dispose();
                __instance.isSongLoadRequested = true;
                __instance.UpdateScoreDisplay();
                __instance.UpdateKanbanSurface();
                __instance.UpdateSortBarSurface();
                __instance.UpdateScoreDisplay();
            }
        }

        /// <summary>
        /// Allow for a song id > 400
        /// </summary>
        [HarmonyPatch(typeof(EnsoMode), "IsValid")]
        [HarmonyPrefix]
        public static bool IsValid_Prefix(ref bool __result, EnsoMode __instance)
        {
#pragma warning disable Harmony003
            __result = Validate();
            return false;
            bool Validate()
            {
                // commented out this code
                // if (songUniqueId < DataConst.InvalidId || songUniqueId > DataConst.MusicMax)
                // {
                //     return false;
                // }
                if (!Enum.IsDefined(typeof(EnsoData.SongGenre), __instance.listGenre))
                {
                    return false;
                }

                if (__instance.neiro < 0 || __instance.neiro > DataConst.NeiroMax)
                {
                    return false;
                }

                if (!Enum.IsDefined(typeof(EnsoData.EnsoLevelType), __instance.level))
                {
                    return false;
                }

                if (!Enum.IsDefined(typeof(DataConst.SpeedTypes), __instance.speed))
                {
                    return false;
                }

                if (!Enum.IsDefined(typeof(DataConst.OptionOnOff), __instance.dron))
                {
                    return false;
                }

                if (!Enum.IsDefined(typeof(DataConst.OptionOnOff), __instance.reverse))
                {
                    return false;
                }

                if (!Enum.IsDefined(typeof(DataConst.RandomLevel), __instance.randomlv))
                {
                    return false;
                }

                if (!Enum.IsDefined(typeof(DataConst.SpecialTypes), __instance.special))
                {
                    return false;
                }

                if (!Enum.IsDefined(typeof(DataConst.SongSortType), __instance.songSortType))
                {
                    return false;
                }

                if (!Enum.IsDefined(typeof(DataConst.SongSortCourse), __instance.songSortCourse))
                {
                    return false;
                }

                if (!Enum.IsDefined(typeof(DataConst.SongFilterType), __instance.songFilterType))
                {
                    return false;
                }

                if (!Enum.IsDefined(typeof(DataConst.SongFilterTypeFavorite), __instance.songFilterTypeFavorite))
                {
                    return false;
                }

                return true;
            }
#pragma warning restore Harmony003
        }

        #endregion

    }
}
