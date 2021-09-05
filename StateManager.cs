// Copyright (c) 2019 v1ld.git@gmail.com
// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Kingmaker;
using Kingmaker.GameModes;
using Kingmaker.PubSubSystem;
using Kingmaker.UI;
using Kingmaker.Utility;

namespace CustomMapMarkers
{
    class StateManager : IWarningNotificationUIHandler, IAreaHandler
    {
        [DataContract]
        internal class SavedState
        {
            [DataMember(Order=1)]
            internal readonly uint Version = 1;   // Data version for serialization
            [DataMember(Order=2)]
            internal uint MarkerNumber = 1;       // Used in creating marker names
            [DataMember(Order=100)]
            internal HashSet<ModGlobalMapLocation> GlobalMapLocations { get; private set; }
            [DataMember(Order=101)]
            internal Dictionary<string, List<ModMapMarker>> AreaMarkers { get; private set; }

            internal string CharacterName { get; private set; }
            internal bool IsLocalMapInitialized = false;

            internal SavedState()
            {
                GlobalMapLocations = new HashSet<ModGlobalMapLocation>();
                AreaMarkers = new Dictionary<string, List<ModMapMarker>>();

                ValidateAfterLoad();
            }

            internal void ValidateAfterLoad()
            {
                if (GlobalMapLocations == null) { GlobalMapLocations = new HashSet<ModGlobalMapLocation>(); }
                if (AreaMarkers == null) { AreaMarkers  = new Dictionary<string, List<ModMapMarker>>(); }
                CharacterName = Game.Instance.Player.MainCharacter.Value?.CharacterName;
                IsLocalMapInitialized = false;
            }

            internal SavedState CleanCopyForSave()
            {
                var clone = (SavedState)this.MemberwiseClone();
                clone.GlobalMapLocations = StateHelpers.PurgeDeletedGlobalMapLocations(this.GlobalMapLocations);
                clone.AreaMarkers = StateHelpers.PurgeDeletedAreaMarkers(this.AreaMarkers);
                return clone;
            }
        }

        internal static SavedState CurrentState;

        internal static void Load()
        {
            EventBus.Subscribe(new StateManager());
        }

        internal static void LoadState()
        {
            Log.Write($"Load request for current=[{CurrentState?.CharacterName}] game=[{Game.Instance.Player.MainCharacter.Value?.CharacterName}]");

            // Load is the best point to handle old format state file names as we only load once, at mod load
            UpdateStateFileName();

            string stateFile = GetStateFilePath();
            if (File.Exists(stateFile))
            {
                try
                {
                    using (FileStream reader = new FileStream(stateFile, FileMode.Open))
                    {
                        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(SavedState));
                        CurrentState = (SavedState)serializer.ReadObject(reader);
                        CurrentState.ValidateAfterLoad();
                        reader.Close();
                        Log.Write($"Loaded state for current=[{CurrentState?.CharacterName}]");
                        return;
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"Failed to load state: {e}");
                    // Move the unreadable state file somewhere safe for now
                    if (File.Exists(stateFile))
                    {
                        string newStateFile = GetStateFilePath("-unreadable-" + Path.GetRandomFileName());
                        File.Move(stateFile, newStateFile);
                        Log.Error($"Moved unreadable state file to {newStateFile}");
                    }
                }
            }

            // Must have a valid state at all times.
            // This catches both first use and load errors.
            CurrentState = new SavedState();
        }

        internal static void SaveState()
        {
            Log.Write($"Save request for current=[{CurrentState?.CharacterName}] game=[{Game.Instance.Player.MainCharacter.Value?.CharacterName}]");

            string gameCharacterName = Game.Instance.Player.MainCharacter.Value?.CharacterName;
            if (gameCharacterName == null || gameCharacterName.Length == 0)
            {
                // Game is exiting
                return;
            }

            if (CurrentState == null || CurrentState.CharacterName != gameCharacterName )
            {
                // New Game
                CurrentState = new SavedState();
            }

            string newStateFile = GetStateFilePath("-new-" + Path.GetRandomFileName());
            try
            {
                using (FileStream writer = new FileStream(newStateFile, FileMode.Create))
                {
                    var savedState = CurrentState.CleanCopyForSave();
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(SavedState));
                    serializer.WriteObject(writer, savedState);
                    writer.Close();     // must explicitly Close() before File.Move()
                    Log.Write($"Saved state for current=[{CurrentState?.CharacterName}]");

                    string originalStateFile = GetStateFilePath();
                    File.Delete(originalStateFile);
                    File.Move(newStateFile, originalStateFile);
                }
            }
            catch (Exception e)
            {
                Log.Error($"Failed to SaveState: {e}");
                if (File.Exists(newStateFile))
                {
                    File.Delete(newStateFile);
                }
            }
        }

        void IWarningNotificationUIHandler.HandleWarning(WarningNotificationType warningType, bool addToLog)
        {
            switch (warningType)
            {
                case WarningNotificationType.GameLoaded:
                    Log.Write($"Load request from [{warningType.ToString()}] event");
                    LoadState();
                    // Game does not send a GameMode event on first load,
                    // this must be checked and handled here!
                    if (Game.Instance.CurrentMode == GameModeType.GlobalMap)
                    {
                        ModGlobalMapLocation.AddGlobalMapLocations();
                    }
                    break;

                case WarningNotificationType.GameSaved:
                case WarningNotificationType.GameSavedAuto:
                case WarningNotificationType.GameSavedQuick:
                    Log.Write($"Save request from [{warningType.ToString()}] event");
                    SaveState();
                    break;
            }
        }

        void IWarningNotificationUIHandler.HandleWarning(string text, bool addToLog) { }

        void IAreaHandler.OnAreaDidLoad()
        {
            Log.Write($"OnAreaDidLoad current=[{CurrentState.CharacterName}] game=[{Game.Instance.Player.MainCharacter.Value?.CharacterName}]");

            if (CurrentState == null || CurrentState.CharacterName != Game.Instance.Player.MainCharacter.Value?.CharacterName)
            {
                LoadState();
            }
        }

        void IAreaHandler.OnAreaBeginUnloading()
        {
            Log.Write($"OnAreaBeginUnloading current=[{CurrentState.CharacterName}] game=[{Game.Instance.Player.MainCharacter.Value?.CharacterName}]");

            string gameCharacterName = Game.Instance.Player.MainCharacter.Value?.CharacterName;
            if (gameCharacterName == null || gameCharacterName.Length == 0)
            {
                // The game does a non-player unload when preparing for a new game, ignore it as there's no player state here
                return;
            }
            if (CurrentState == null || CurrentState.CharacterName != gameCharacterName)
            {
                CurrentState = new SavedState();
            }
            SaveState();
            CustomMapMarkers.RemoveMarkersFromLocalMap();
        }

        private static string StateFilenameBase = "custom-map-markers-state";
        private static string StateFilenameExt = ".json";

        private static string GetStateFilePath(string suffix = "")
        {
            string characterFileName = GetCharacterFileName(StateFilenameBase);

            return Path.Combine(ApplicationPaths.persistentDataPath, characterFileName + suffix + StateFilenameExt);
        }

        private static void UpdateStateFileName()
        {
            string oldFilePath = Path.Combine(ApplicationPaths.persistentDataPath, StateFilenameBase + StateFilenameExt);
            string newFilePath = GetStateFilePath();
            try
            {
                // Check for old, characterName-less filename and rename to new name iff new file doesn't exist
                if (!File.Exists(newFilePath) && File.Exists(oldFilePath))
                {
                    File.Move(oldFilePath, newFilePath);
                }
            }
            catch (Exception e)
            {
                Log.Error($"Problem renaming file old=[{oldFilePath}] new=[{newFilePath}]:\n{e}");
            }
        }

        private static Dictionary<string, string> FileNameForChar = new Dictionary<string, string>();

        private static string GetCharacterFileName(string prefix)
        {
            string characterName = Game.Instance.Player.MainCharacter.Value?.CharacterName;
            if (characterName == null || characterName.Length == 0)
            {
                characterName = "Unnamed";   // Unnamed is what the game itself uses
            }

            if (!FileNameForChar.ContainsKey(characterName) || FileNameForChar[characterName] == null || FileNameForChar[characterName].Length == 0)
            {
                StringBuilder safeName = new StringBuilder(characterName.Length);
                for (int i = 0; i < characterName.Length; i++)
                {
                    safeName.Append(Path.GetInvalidFileNameChars().Contains(characterName[i]) ? '_' : characterName[i]);
                }
                FileNameForChar[characterName] = prefix + "-" + safeName.ToString();
            }

            return FileNameForChar[characterName];
        }
    }

    class StateHelpers
    {
        internal static Dictionary<string, List<ModMapMarker>> PurgeDeletedAreaMarkers(Dictionary<string, List<ModMapMarker>> oldMarkers)
        {
            Dictionary<string, List<ModMapMarker>> newMarkers = new Dictionary<string, List<ModMapMarker>>();
            foreach (var area in oldMarkers.Keys)
            {
                if (oldMarkers[area].Any(marker => marker.IsDeleted))
                {
                    List<ModMapMarker> markers = oldMarkers[area].FindAll(marker => !marker.IsDeleted);
                    if (markers.Count() > 0)
                    {
                        newMarkers[area] = markers;
                    }
                }
                else
                {
                    newMarkers[area] = oldMarkers[area];
                }
            }
            return newMarkers;
        }

        internal static HashSet<ModGlobalMapLocation> PurgeDeletedGlobalMapLocations(HashSet<ModGlobalMapLocation> oldLocations)
        {
            HashSet<ModGlobalMapLocation> newLocations;
            if (oldLocations.Any(location => location.IsDeleted))
            {
                newLocations = new HashSet<ModGlobalMapLocation>(oldLocations.Where(location => !location.IsDeleted));
            }
            else
            {
                newLocations = oldLocations;
            }
            return newLocations;
        }
    }
}
