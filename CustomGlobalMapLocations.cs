// Copyright (c) 2019 v1ld.git@gmail.com
// This code is licensed under MIT license (see LICENSE for details)

using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.GameModes;
using Kingmaker.Globalmap;
using Kingmaker.Globalmap.Blueprints;
using Kingmaker.Globalmap.View;
using Kingmaker.PubSubSystem;
using Kingmaker.UI;
using UnityEngine;
using Owlcat.Runtime.Visual.RenderPipeline.RendererFeatures.Highlighting;
using Kingmaker.Utility;

namespace CustomMapMarkers
{
    class CustomGlobalMapLocations : IGameModeHandler
    {
        internal static void Load()
        {
            EventBus.Subscribe(new CustomGlobalMapLocations());
            //ModGlobalMapLocation.AddGlobalMapLocations();
        }

        internal static void CustomizeGlobalMapLocation(GlobalMapPointView location)
        {
            ModGlobalMapLocation mapLocation = ModGlobalMapLocation.FindOrCreateByAssetGuid(location.Blueprint.AssetGuid);
            if (mapLocation != null)
            {
                mapLocation.UpdateGlobalMapLocation();
                UISoundController.Instance.Play(UISoundType.ButtonClick);
            }
            else
            {
                Log.Error($"Could not findOrCreate location name=[{location.Blueprint.GetName(false)}] guid=[{location.Blueprint.AssetGuid}]");
            }
        }

        void IGameModeHandler.OnGameModeStart(GameModeType gameMode)
        {
            if (gameMode == GameModeType.GlobalMap)
            {
                ModGlobalMapLocation.AddGlobalMapLocations();
            }
        }

        void IGameModeHandler.OnGameModeStop(GameModeType gameMode)
        {
        }

        internal static void PostUpdateHighlight(GlobalMapPointView location)
        {
            ModGlobalMapLocation gL = ModGlobalMapLocation.FindByAssetGuid(location.Blueprint.AssetGuid);
            if (gL != null && gL.IsVisible && !Helpers.GetField<CountableFlag>(location, "m_Hover").Value && !(bool)Helpers.GetField(location, "m_UiSelected"))
            {
#if DEBUG
                Log.Write($"gL Is Visible and all is fine");
#endif
                Highlighter[] highlighters;
                highlighters = location.GetComponentsInChildren<Highlighter>();
                for (int i = 0; i < highlighters.Length; i++)
                {
                    highlighters[i].ConstantOnImmediate(gL.Color);
                }

            }


        }
    }


    [DataContract]
    class ModGlobalMapLocation
	{
        private static HashSet<ModGlobalMapLocation> GlobalMapLocations { get { return StateManager.CurrentState.GlobalMapLocations; } }

        [DataMember]
        internal string Name;
        [DataMember]
        internal string Notes;
        [DataMember]
        internal Color Color;
        [DataMember]
        internal bool IsVisible;
        [DataMember]
        private string AssetGuid;

        private GlobalMapPointView mapLocation;
        internal bool IsDeleted = false;
        internal bool IsBeingDeleted = false;

        private ModGlobalMapLocation(GlobalMapPointView location)
        {
            this.mapLocation = location;
            this.AssetGuid = location.Blueprint.AssetGuid.ToString();

            this.Name = mapLocation.Blueprint.GetName(false);
            this.Notes = $"Custom location #{StateManager.CurrentState.MarkerNumber++}";
            this.Color = Color.green;
            this.IsVisible = true;

            GlobalMapLocations.Add(this);
        }

        internal static ModGlobalMapLocation FindOrCreateByAssetGuid(BlueprintGuid assetGuid)
        {
            var modLocation = GlobalMapLocations.FirstOrDefault(location => location.AssetGuid.ToString() == assetGuid.ToString());
            if (modLocation == null)
            {
                GlobalMapPointView mapLocation = GlobalMapPointView.Instances.FirstOrDefault(map => map.Blueprint.AssetGuid == assetGuid);
                if (mapLocation != null)
                {
                    modLocation = new ModGlobalMapLocation(mapLocation);
#if DEBUG
                    Log.Write($"Created new GlobalMapLocation for assetGuid=[{assetGuid}]");
#endif
                }
                else
                {
                    Log.Write($"Cannot find GlobalMapLocation for assetGuid=[{assetGuid}]");
                }
            }
            return modLocation;
        }

        internal static ModGlobalMapLocation FindByAssetGuid(BlueprintGuid assetGuid)
            => GlobalMapLocations.FirstOrDefault(location => location.AssetGuid.ToString() == assetGuid.ToString());

        internal static string GetModifiedDescription(BlueprintGlobalMapPoint bpLocation, string result)
        {
            ModGlobalMapLocation mapLocation = GlobalMapLocations.FirstOrDefault(location => location.AssetGuid.ToString() == bpLocation.AssetGuid.ToString());
            if (mapLocation != null && !mapLocation.IsDeleted && mapLocation.IsVisible)
            {
                return result + "\n\n" + $"<b>Notes\n</b> <i>{mapLocation.Notes}</i>";
            }
            else
            {
                return result;
            }
        }

        internal bool UpdateGlobalMapLocation()
        {
            if (this.mapLocation == null)
            {
                this.mapLocation = GlobalMapPointView.Instances.FirstOrDefault(map => map.Blueprint.AssetGuid == this.AssetGuid);
                if (this.mapLocation == null)
                {
                    Log.Error($"Cannot find GlobalMapLocation for assetGuid=[{this.AssetGuid}]");
                    return false;
                }
            }
            if (this.Name == null)
            {
                this.Name = mapLocation.Blueprint.GetName(false);
            }

            /* Triggers a Bug which keeps anything from being selected
             * 
             * Possible Fix: Override/alter  GlobalMapPointView.UpdateHighlight() ? Gotta think about it.
             * 
             * 
            this.mapLocation.HoverColor = this.IsVisible ? this.Color : this.mapLocation.CurrentColor;
            this.mapLocation.OverrideHCol = this.IsVisible;

            // Don't have a direct way to set a highlight color on the map icon,
            // so fake it by marking customized locations as being hovered.
            Helpers.SetField(this.mapLocation, "m_Hover", this.IsVisible);

            */
            this.mapLocation.UpdateHighlight();
            return true;
        }


        internal static void AddGlobalMapLocations()
        {
            foreach (var location in GlobalMapLocations)
            {
                if (!location.UpdateGlobalMapLocation())
                {
                    Log.Error($"Malformed location=[{location.AssetGuid}]");
                }
            }
        }
    }

    class CustomGlobalMapLocationsMenu
    {
        private static HashSet<ModGlobalMapLocation> GlobalMapLocations { get { return StateManager.CurrentState.GlobalMapLocations; } }
        private static string[] ColorNames = { "Black", "Blue", "Cyan", "Gray", "Green", "Magenta", "Red", "White", "Yellow" };
        private static Color[] Colors = { Color.black, Color.blue, Color.cyan, Color.gray, Color.green, Color.magenta, Color.red, Color.white, Color.yellow };

        internal static void Layout()
        {
            var fixedWidth = new GUILayoutOption[1] { GUILayout.ExpandWidth(false) };

            GUILayout.Label("<b><color=cyan>Descriptions can have multiple lines and paragraphs.</color></b>", fixedWidth);

            uint locationNumber = 1;
            foreach (var location in GlobalMapLocations)
            {
                if (location.IsDeleted) { continue; }

                GUILayout.Space(10f);

                string locationLabel = $"{locationNumber++}: { location.Name ?? location.Notes }";
                if (location.IsVisible) { locationLabel = $"<color=#1aff1a><b>{locationLabel}</b></color>"; }
                GUILayout.Label(locationLabel, fixedWidth);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Notes: ", fixedWidth);
                location.Notes = GUILayout.TextArea(location.Notes, GUILayout.MaxWidth(500f));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Color: ", fixedWidth);
                for (int i = 0; i < ColorNames.Length; i++)
                {
                    if (GUILayout.Toggle(location.Color == Colors[i], ColorNames[i], fixedWidth))
                    {
                        location.Color = Colors[i];
                        location.UpdateGlobalMapLocation();
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(location.IsVisible ? "Hide" : "Show", fixedWidth))
                {
                    location.IsVisible = !location.IsVisible;
                }
                if (!location.IsBeingDeleted && GUILayout.Button("Delete", fixedWidth))
                {
                    location.IsBeingDeleted = true;
                }
                if (location.IsBeingDeleted)
                {
                    GUILayout.Label("Are you sure?", fixedWidth);
                    if (GUILayout.Button("Yes", fixedWidth))
                    {
                        location.IsDeleted = true;
                        location.IsVisible = false;
                        location.UpdateGlobalMapLocation();
                    }
                    if (GUILayout.Button("No", fixedWidth))
                    {
                        location.IsBeingDeleted = false;
                    }
                }
                GUILayout.EndHorizontal();
            }
        }
    }
}
