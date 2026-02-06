using System;
using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TakoTako.Patches;
using UnityEngine;
using SaveProfileManager;

using SaveProfileManager.Patches;
using System.Reflection;
using System.IO;

#if TAIKO_IL2CPP
using BepInEx.Unity.IL2CPP.Utils;
using BepInEx.Unity.IL2CPP;
#endif

#pragma warning disable BepInEx002
namespace TakoTako
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
#if TAIKO_MONO
    public class Plugin : BaseUnityPlugin
#elif TAIKO_IL2CPP
    public class Plugin : BasePlugin
#endif
    {
        public const string ModName = "TakoTako";

        public ConfigEntry<bool> ConfigSkipSplashScreen;
        public ConfigEntry<bool> ConfigAutomaticallyStartGame;
        public ConfigEntry<bool> ConfigDisableScreenChangeOnFocus;
        public ConfigEntry<bool> ConfigFixSignInScreen;
        public ConfigEntry<bool> ConfigEnableCustomSongs;
        public ConfigEntry<bool> ConfigEnableTaikoDrumSupport;
        public ConfigEntry<bool> ConfigTaikoDrumUseNintendoLayout;
        public ConfigEntry<bool> ConfigSkipDLCCheck;

        public ConfigEntry<string> ConfigSongDirectory;
        public ConfigEntry<bool> ConfigSaveEnabled;
        public ConfigEntry<string> ConfigSaveDirectory;
        public ConfigEntry<string> ConfigOverrideDefaultSongLanguage;
        public ConfigEntry<bool> ConfigApplyGenreOverride;

        public static Plugin Instance;
        private Harmony _harmony;
        public new static ManualLogSource Log;

        // private ModMonoBehaviourHelper _modMonoBehaviourHelper;

#if TAIKO_MONO
        private void Awake()
#elif TAIKO_IL2CPP
        public override void Load()
#endif
        {
            Instance = this;

#if TAIKO_MONO
            Log = Logger;
#elif TAIKO_IL2CPP
            Log = base.Log;
#endif


            SetupConfig(Config, Path.Combine("BepInEx", "data", ModName));
            SetupHarmony();

            var isSaveManagerLoaded = IsSaveManagerLoaded();
            if (isSaveManagerLoaded)
            {
                AddToSaveManager();
            }
        }

        private void SetupConfig(ConfigFile config, string saveFolder, bool isSaveManager = false)
        {
            string dataFolder = Path.Combine("BepInEx", "data", ModName);


            ConfigEnableCustomSongs = config.Bind("CustomSongs",
                "EnableCustomSongs",
                true,
                "When true this will load custom mods");

            ConfigSongDirectory = config.Bind("CustomSongs",
                "SongDirectory",
                $"{dataFolder}/customSongs",
                "The directory where custom tracks are stored");

            ConfigSaveEnabled = config.Bind("CustomSongs",
                "SaveEnabled",
                true,
                "Should there be local saves? Disable this if you want to wipe modded saves with every load");

            ConfigSaveDirectory = config.Bind("CustomSongs",
                "SaveDirectory",
                $"{saveFolder}/saves",
                "The directory where saves are stored");

            ConfigOverrideDefaultSongLanguage = config.Bind("CustomSongs",
                "ConfigOverrideDefaultSongLanguage",
                string.Empty,
                "Set this value to {Japanese, English, French, Italian, German, Spanish, ChineseTraditional, ChineseSimplified, Korean} " +
                "to override all music tracks to a certain language, regardless of your applications language");

            ConfigApplyGenreOverride = config.Bind("CustomSongs",
                "ConfigApplyGenreOverride",
                true,
                "Set this value to {01 Pop, 02 Anime, 03 Vocaloid, 04 Children and Folk, 05 Variety, 06 Classical, 07 Game Music, 08 Live Festival Mode, 08 Namco Original} " +
                "to override all track's genre in a certain folder. This is useful for TJA files that do not have a genre");

            ConfigFixSignInScreen = config.Bind("General",
                "FixSignInScreen",
                false,
                "When true this will apply the patch to fix signing into Xbox Live");

            ConfigSkipSplashScreen = config.Bind("General",
                "SkipSplashScreen",
                true,
                "When true this will skip the intro");
            
            ConfigAutomaticallyStartGame = config.Bind("General",
                "AutomaticallyStartGame",
                false,
                "When true this will continue on the main menu ");
            
            ConfigSkipDLCCheck = config.Bind("General",
                "SkipDLCCheck",
                true,
                "When true this will skip slow DLC checks");

            ConfigDisableScreenChangeOnFocus = config.Bind("General",
                "DisableScreenChangeOnFocus",
                false,
                "When focusing this wont do anything jank, I thnk");

            ConfigEnableTaikoDrumSupport = config.Bind("Controller.TaikoDrum",
                "ConfigEnableTaikoDrumSupport",
                true,
                "This will enable support for Taiko drums, current tested with official Hori Drum");

            ConfigTaikoDrumUseNintendoLayout = config.Bind("Controller.TaikoDrum",
                "ConfigTaikoDrumUseNintendoLayout",
                false,
                "This will use the Nintendo layout YX/BA for the Hori Taiko Drum");
        }

        private void SetupHarmony()
        {
            // Patch methods
            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);

            LoadPlugin(true);
        }

        public static void LoadPlugin(bool enabled)
        {
            if (enabled)
            {
                bool result = true;
                // If any PatchFile fails, result will become false
                //result &= Instance.PatchFile(typeof(ExampleSingleHitBigNotesPatch));
                //result &= Instance.PatchFile(typeof(ExampleSortByUraPatch));

                if (Instance.ConfigSkipSplashScreen.Value)
                    result &= Instance.PatchFile(typeof(SkipSplashScreenPatch));

                if (Instance.ConfigAutomaticallyStartGame.Value)
                    result &= Instance.PatchFile(typeof(AutomaticallyStartGamePatch));

                if (Instance.ConfigFixSignInScreen.Value)
                    result &= Instance.PatchFile(typeof(SignInPatch));

                if (Instance.ConfigDisableScreenChangeOnFocus.Value)
                    result &= Instance.PatchFile(typeof(DisableScreenChangeOnFocusPatch));

                if (Instance.ConfigEnableTaikoDrumSupport.Value)
                    result &= Instance.PatchFile(typeof(TaikoDrumSupportPatch));

#if TAIKO_IL2CPP
                if (ConfigSkipDLCCheck.Value)
                    result &= Instance.PatchFile(typeof(SkipDLCCheckPatch));
#endif

                if (Instance.ConfigEnableCustomSongs.Value)
                {
                    result &= Instance.PatchFile(typeof(CustomMusicLoaderPatch));
                    CustomMusicLoaderPatch.Setup();
                }


                if (result)
                {
                    ModLogger.Log($"Plugin {MyPluginInfo.PLUGIN_NAME} is loaded!");
                }
                else
                {
                    ModLogger.Log($"Plugin {MyPluginInfo.PLUGIN_GUID} failed to load.", LogType.Error);
                    // Unload this instance of Harmony
                    // I hope this works the way I think it does
                    Instance._harmony.UnpatchSelf();
                }
            }
            else
            {
                ModLogger.Log($"Plugin {MyPluginInfo.PLUGIN_NAME} is disabled.");
            }
        }

        private bool PatchFile(Type type)
        {
            if (_harmony == null)
            {
                _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            }
            try
            {
                _harmony.PatchAll(type);
                ModLogger.Log("File patched: " + type.FullName, LogType.Debug);
                return true;
            }
            catch (Exception e)
            {
                ModLogger.Log("Failed to patch file: " + type.FullName);
                ModLogger.Log(e.Message);
                return false;
            }
        }

        public static void UnloadPlugin()
        {
            CustomMusicLoaderPatch.UnloadCustomSongs();
            CustomMusicLoaderPatch.UnloadSaveData();
            Instance._harmony.UnpatchSelf();
            ModLogger.Log($"Plugin {MyPluginInfo.PLUGIN_NAME} has been unpatched.");
        }

        public static void ReloadPlugin()
        {
            // Reloading will always be completely different per mod
            // You'll want to reload any config file or save data that may be specific per profile
            // If there's nothing to reload, don't put anything here, and keep it commented in AddToSaveManager

            CustomMusicLoaderPatch.Setup();
            CustomMusicLoaderPatch.ReloadCustomSongs();
            CustomMusicLoaderPatch.ReloadSaveData();
            ModLogger.Log($"Plugin {MyPluginInfo.PLUGIN_NAME} has been reloaded.");
        }

        public void AddToSaveManager()
        {
            // Add SaveDataManager dll path to your csproj.user file
            // https://github.com/Deathbloodjr/TDMX.SaveProfileManager
            var plugin = new PluginSaveDataInterface(MyPluginInfo.PLUGIN_GUID);
            plugin.AssignLoadFunction(LoadPlugin);
            plugin.AssignUnloadFunction(UnloadPlugin);

            // Reloading will always be completely different per mod
            // You'll want to reload any config file or save data that may be specific per profile
            // If there's nothing to reload, don't put anything here, and keep it commented in AddToSaveManager
            plugin.AssignReloadSaveFunction(ReloadPlugin);

            // Uncomment this if there are more config options than just ConfigEnabled
            plugin.AssignConfigSetupFunction(SetupConfig);
            plugin.AddToManager(true);
        }

        private bool IsSaveManagerLoaded()
        {
            try
            {
                Assembly loadedAssembly = Assembly.Load("com.DB.TDMX.SaveProfileManager");
                return loadedAssembly != null;
            }
            catch
            {
                return false;
            }
        }

        public static MonoBehaviour GetMonoBehaviour() => TaikoSingletonMonoBehaviour<CommonObjects>.Instance;

        public void StartCustomCoroutine(IEnumerator enumerator)
        {
            GetMonoBehaviour().StartCoroutine(enumerator);
        }
    }
}
