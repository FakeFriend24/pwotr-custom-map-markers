// Copyright (c) 2019 v1ld.git@gmail.com
// Copyright (c) 2019 Jennifer Messerly
// This code is licensed under MIT license (see LICENSE for details)

using System; 
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Root.Strings.GameLog;
using Kingmaker.GameModes;
using Kingmaker.Globalmap;
using Kingmaker.Globalmap.Blueprints;
using Kingmaker.Globalmap.View;
using Kingmaker.PubSubSystem;
using Kingmaker.UI.ServiceWindow.LocalMap;
using Kingmaker.Utility;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityModManagerNet;
using Owlcat.Runtime.Core;
using Owlcat.Runtime.Core.Logging;
using Kingmaker.UI;
using Kingmaker.UI.MVVM._PCView.ServiceWindows.LocalMap;
using Kingmaker.UI.MVVM._VM.ServiceWindows.LocalMap;
using HarmonyLib;

namespace CustomMapMarkers
{
    public class Main
    {

        [HarmonyPatch(typeof(LocalMapPCView))]
        [HarmonyPatch("OnPointerClick")]
        static class LocalMapPCView_OnPointerClick_Patch
        {

            private static bool Prefix(LocalMapPCView __instance, PointerEventData eventData)
            {
#if DEBUG
                // Perform extra sanity checks in debug builds.

                Log.Write($"Prefix executed. PointerClick was registered");
#endif
                if (eventData.button == PointerEventData.InputButton.Left)
                {
#if DEBUG
                    // Perform extra sanity checks in debug builds.

                    Log.Write($"PointerClick is left Button ");
#endif
                    if (KeyboardAccess.IsShiftHold())
                    {
#if DEBUG
                        // Perform extra sanity checks in debug builds.

                        Log.Write($"Shift has been Hold too. " + (__instance == null).ToString());
#endif
                        CustomMapMarkers.CreateMarker(__instance, eventData);
                    }
                }

                // Don't pass the click through to the map if control or shift are pressed
                return !(KeyboardAccess.IsCtrlHold() || KeyboardAccess.IsShiftHold());
            }
        }

        [HarmonyPatch(typeof(LocalMapPCView))]
        [HarmonyPatch("Update")]
        static class LocalMapPCView_Update_Patch
        {
            private static bool Prefix(LocalMapPCView __instance)
            {
                if (Traverse.Create(__instance).Field<bool>("m_MouseDown").Value)
                {
                    return !(KeyboardAccess.IsCtrlHold() || KeyboardAccess.IsShiftHold());
                }                // Don't pass the click through to the map if control or shift are pressed
                return true;
            }
        }

        [HarmonyPatch(typeof(LocalMapPCView))]
        [HarmonyPatch("BindViewImplementation")]
        static class LocalMapPCView_BindViewImplementation_Patch
        {
            private static void Postfix(LocalMapPCView __instance)
            {
                IsLocalMapActive = true;
                CustomMapMarkers.OnShowLocalMap(__instance);
            }
        }

        [HarmonyPatch(typeof(LocalMapPCView))]
        [HarmonyPatch("DestroyViewImplementation")]
        static class LocalMapPCView_DestroyViewImplementation_Patch
        {
            private static void Postfix()
            {
                IsLocalMapActive = false;
            }
        }

        [HarmonyPatch(typeof(GlobalMapPointView))]
        [HarmonyPatch("HandleClick")]
        static class GlobalMapPointView_HandleClick_Patch
        {
            private static bool Prefix(GlobalMapPointView __instance)
            {
                if (KeyboardAccess.IsShiftHold())
                {
                    CustomGlobalMapLocations.CustomizeGlobalMapLocation(__instance);
                }
                // Don't pass the click through to the map if control or shift are pressed
                return !(KeyboardAccess.IsCtrlHold() || KeyboardAccess.IsShiftHold());
            }
        }

        /*
        [HarmonyPatch(typeof(GlobalMapPointView))]
        [HarmonyPatch("IGMPoint.OnPointerClick")]
        static class GlobalMapPointView_OnPointerClick_Patch
        {
            private static bool Prefix(GlobalMapPointView __instance)
            {
                // Don't pass the click through to the map if control or shift are pressed
                return !(KeyboardAccess.IsCtrlHold() || KeyboardAccess.IsShiftHold());
            }
        }
        */
        [HarmonyPatch(typeof(GlobalMapPointView))]
        [HarmonyPatch("UpdateHighlight")]
        static class GlobalMapPointView_UpdateHighlight_Patch
        {
            private static void Postfix(GlobalMapPointView __instance)
            {
                CustomGlobalMapLocations.PostUpdateHighlight(__instance);
            }
        }
        /* Hovering is now handled differently, gotta intercept UpdateHighlight Now
        [HarmonyPatch(typeof(GlobalMapPointView))]
        [HarmonyPatch("HandleHoverChange")]
        static class GlobalMapLocation_HandleHoverChange_Patch
        {
            private static void Postfix(GlobalMapPointView __instance, bool isHover)
            {
                CustomGlobalMapLocations.PostHandleHoverchange(__instance, isHover);
            }
        } 
        */
        [HarmonyPatch(typeof(BlueprintGlobalMapPoint))]
        [HarmonyPatch("GetDescription")]
        static class BlueprintLocation_GetDescription_Patch
        {
            private static void Postfix(BlueprintGlobalMapPoint __instance, ref string __result)
            {
                __result = ModGlobalMapLocation.GetModifiedDescription(__instance, __result);
            }
        }

        [HarmonyPatch(typeof(UnityModManager.UI))]
        [HarmonyPatch("Update")]

        static class UnityModManager_UI_Update_Patch
        {
            private static void Postfix()
            {
                if (IsLocalMapActive || Game.Instance.CurrentMode == GameModeType.GlobalMap)
                {
                    try
                    {
                        IsControlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                        IsShiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    }
                    catch (Exception e)
                    {
                        Log.Write($"Key read: {e}");
                    }
                }
            }
        }

        private static bool IsLocalMapActive = false;
        private static bool IsControlPressed = false;
        private static bool IsShiftPressed = false;

        public static bool enabled;

        public static UnityModManager.ModEntry.ModLogger logger;

        static Settings settings;

        static Harmony harmonyInstance;

        static readonly Dictionary<Type, bool> typesPatched = new Dictionary<Type, bool>();
        static readonly List<String> failedPatches = new List<String>();
        static readonly List<String> failedLoading = new List<String>();

        [System.Diagnostics.Conditional("DEBUG")]
        static void EnableGameLogging()
        {
            if (Owlcat.Runtime.Core.Logging.Logger.Instance.Enabled) return;

            // Code taken from GameStarter.Awake(). PF:K logging can be enabled with command line flags,
            // but when developing the mod it's easier to force it on.
            var dataPath = ApplicationPaths.persistentDataPath;
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Owlcat.Runtime.Core.Logging.Logger.Instance.Enabled = true;
            var text = Path.Combine(dataPath, "GameLog.txt");
            if (File.Exists(text))
            {
                File.Copy(text, Path.Combine(dataPath, "GameLogPrev.txt"), overwrite: true);
                File.Delete(text);
            }
            Owlcat.Runtime.Core.Logging.Logger.Instance.AddLogger(new UberLoggerFile("GameLogFull.txt", dataPath));
            Owlcat.Runtime.Core.Logging.Logger.Instance.AddLogger(new UberLoggerFilter(new UberLoggerFile("GameLog.txt", dataPath), LogSeverity.Warning, "MatchLight"));

            Owlcat.Runtime.Core.Logging.Logger.Instance.Enabled = true;
        }

        internal static void NotifyPlayer(string message, bool warning = false)
        {
            if (warning)
            {
                EventBus.RaiseEvent<IWarningNotificationUIHandler>((IWarningNotificationUIHandler h) => h.HandleWarning(message, true));
            }
            else
            {
                // Game.Instance.UI.DBattleLogManager.LogView.AddLogEntry(message, GameLogStrings.Instance.DefaultColor);
            }
        }


        // ** TO-DO: Update individualPatching to Harmony2 **//
        /*
        // We don't want one patch failure to take down the entire mod, so they're applied individually.
        //
        // Also, in general the return value should be ignored. If a patch fails, we still want to create
        // blueprints, otherwise the save won't load. Better to have something be non-functional.
        static bool ApplyPatch(Type type, String featureName)
        {
            try
            {
                if (typesPatched.ContainsKey(type)) return typesPatched[type];

                var patchInfo = HarmonyMethodExtensions.GetHarmonyMethods(type);
                if (patchInfo == null || patchInfo.Count() == 0)
                {
                    Log.Error($"Failed to apply patch {type}: could not find Harmony attributes");
                    failedPatches.Add(featureName);
                    typesPatched.Add(type, false);
                    return false;
                }
                var processor = new Harmony12.PatchProcessor(harmonyInstance, type, Harmony12.HarmonyMethod.Merge(patchInfo));
                var patch = processor.Patch().FirstOrDefault();
                if (patch == null)
                {
                    Log.Error($"Failed to apply patch {type}: no dynamic method generated");
                    failedPatches.Add(featureName);
                    typesPatched.Add(type, false);
                    return false;
                }
                typesPatched.Add(type, true);
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Failed to apply patch {type}: {e}");
                failedPatches.Add(featureName);
                typesPatched.Add(type, false);
                return false;
            }
        }

        static void CheckPatchingSuccess()
        {
            // Check to make sure we didn't forget to patch something.
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                var infos = Harmony12.HarmonyMethodExtensions.GetHarmonyMethods(type);
                if (infos != null && infos.Count() > 0 && !typesPatched.ContainsKey(type))
                {
                    Log.Write($"Did not apply patch for {type}");
                }
            }
        }
        */


        // mod entry point, invoked from UMM
        static bool Load(UnityModManager.ModEntry modEntry)
        {
            logger = modEntry.Logger;
            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            harmonyInstance = new Harmony(modEntry.Info.Id);
            harmonyInstance.PatchAll();
            //if (!applypatch(typeof(libraryscriptableobject_loaddictionary_patch), "load library"))
            //{
            //    throw error("failed to patch libraryscriptableobject.loaddictionary(), cannot load mod");
            //}
            /*
            if (!ApplyPatch(typeof(UnityModManager_UI_Update_Patch), "Read keys"))
            {
                throw Error("Failed to patch LibraryScriptableObject.LoadDictionary(), cannot load mod");
            }
            if (!ApplyPatch(typeof(LocalMapPCView_OnPointerClick_Patch), "Local map click"))
            {
                throw Error("Failed to patch LocalMap.OnPointerClick(), cannot load mod");
            }
            if (!ApplyPatch(typeof(LocalMapPCView_BindViewImplementation_Patch), "Local map show"))
            {
                throw Error("Failed to patch LocalMap.BindViewImplementation(), cannot load mod");
            }
            if (!ApplyPatch(typeof(LocalMapPCView_DestroyViewImplementation_Patch), "Local map hide"))
            {
                throw Error("Failed to patch LocalMap.DestroyViewImplementation(), cannot load mod");
            }
            if (!ApplyPatch(typeof(GlobalMapLocation_HandleClick_Patch), "Global map location click"))
            {
                throw Error("Failed to patch GlobalMapLocation.HandleClick(), cannot load mod");
            }
            if (!ApplyPatch(typeof(GlobalMapLocation_HandleHoverChange_Patch), "Global map hover change"))
            {
                throw Error("Failed to patch GlobalMapLocation.HandleHoverChange(), cannot load mod");
            }
            if (!ApplyPatch(typeof(BlueprintLocation_GetDescription_Patch), "Blueprint location description"))
            {
                throw Error("Failed to patch BlueprintLocation.GetDescription(), cannot load mod");
            }
            */
            StartMod();
            return true;
        }

        static void StartMod()
        {
            SafeLoad(StateManager.Load, "State Manager");
            SafeLoad(CustomMapMarkers.Load, "Local Map Markers");
            SafeLoad(CustomGlobalMapLocations.Load, "Global Map Locations");
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            enabled = value;
            return true;
        }

        private static bool editingGlobalMapLocations = true;

        static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            if (!enabled) return;

            var fixedWidth = new GUILayoutOption[1] { GUILayout.ExpandWidth(false) };
            if (failedPatches.Count > 0)
            {
                GUILayout.BeginVertical();
                GUILayout.Label("<b><color=#ff5454>Error: Some patches failed to apply. These features may not work:</color></b>", fixedWidth);
                foreach (var featureName in failedPatches)
                {
                    GUILayout.Label($"  • <b><color=#ff5454>{featureName}</b>", fixedWidth);
                }
                GUILayout.EndVertical();
            }
            if (failedLoading.Count > 0)
            {
                GUILayout.BeginVertical();
                GUILayout.Label("<b><color=#ff5454>Error: Some assets failed to load. Saves using these features won't work:</color></b>", fixedWidth);
                foreach (var featureName in failedLoading)
                {
                    GUILayout.Label($"  • <b><color=#ff5454>{featureName}</color></b>", fixedWidth);
                }
                GUILayout.EndVertical();
            }
#if DEBUG
            GUILayout.BeginVertical();
            GUILayout.Label("<b>DEBUG build!</b>", fixedWidth);
            GUILayout.Space(10f);
            GUILayout.EndVertical();
#endif

            string gameCharacterName = Game.Instance.Player.MainCharacter.Value?.CharacterName;
            if (gameCharacterName == null || gameCharacterName.Length == 0)
            {
                GUILayout.Label("<b><color=#ff5454>Load a save to edit map notes and markers.</color></b>", fixedWidth);
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("<b><color=cyan>Edit: </color></b>", fixedWidth);
            bool doEditLocalMap = GUILayout.Toggle(!editingGlobalMapLocations, "<b><color=cyan>Local Map Areas</color></b>", fixedWidth);
            editingGlobalMapLocations = GUILayout.Toggle(!doEditLocalMap, "<b><color=cyan>Global Map Locations</color></b>", fixedWidth);
            GUILayout.EndHorizontal();

            if (editingGlobalMapLocations)
            {
                CustomGlobalMapLocationsMenu.Layout();
            }
            else
            {
                CustomMapMarkersMenu.Layout();
            }
        }

        static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            settings.Save(modEntry);
            StateManager.SaveState();
        }

        static void SafeLoad(Action load, String name)
        {
            try
            {
                load();
            }
            catch (Exception e)
            {
                failedLoading.Add(name);
                Log.Error(e);
            }
        }

        static T SafeLoad<T>(Func<T> load, String name)
        {
            try
            {
                return load();
            }
            catch (Exception e)
            {
                failedLoading.Add(name);
                Log.Error(e);
                return default(T);
            }
        }

        static Exception Error(String message)
        {
            logger?.Log(message);
            return new InvalidOperationException(message);
        }
    }


    public class Settings : UnityModManager.ModSettings
    {
        public bool SaveAfterEveryChange = false;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            UnityModManager.ModSettings.Save<Settings>(this, modEntry);
        }
    }
}
