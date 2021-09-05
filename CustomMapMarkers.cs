// Copyright (c) 2019 v1ld.git@gmail.com
// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Kingmaker;
using Kingmaker.PubSubSystem;
using Kingmaker.UI;
using Kingmaker.UI.MVVM._PCView.ServiceWindows.LocalMap;
using Kingmaker.UI.MVVM._VM.ServiceWindows.LocalMap;
using Kingmaker.UI.MVVM._VM.ServiceWindows.LocalMap.Utils;
using Kingmaker.UI.ServiceWindow.LocalMap;
using Kingmaker.Visual.LocalMap;
using Owlcat.Runtime.UI.MVVM;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomMapMarkers
{
    class CustomMapMarkers
    {
        private static Dictionary<string, List<ModMapMarker>> AreaMarkers { get { return StateManager.CurrentState.AreaMarkers; } }

        internal static void Load()
        {
            EventBus.Subscribe(new CustomMapMarkers());
        }

        internal static void OnShowLocalMap()
        {
            AddMarkerstoLocalMap();
        }

        private static FastInvoke LocalMapVM_OnUpdateHandler = Helpers.CreateInvoker<LocalMapVM>("OnUpdateHandler");

        private static FastInvoke LocalMapPCView_BindViewImplementation = Helpers.CreateInvoker<LocalMapPCView>("BindViewImplementation");

        internal static void CreateMarker(LocalMapPCView map, PointerEventData eventData)
        {
            ModMapMarker marker = NewMarker(map, eventData);
            LocalMapModel.Markers.Add(marker);

            //LocalMapVM_OnUpdateHandler(map.GetPropertyValue("ViewModel") as LocalMapVM);  // Force a refresh to display the new mark
            LocalMapPCView_BindViewImplementation(map);  // Force a refresh to display the new mark
            UISoundController.Instance.Play(UISoundType.ButtonClick);
        }

        private static ModMapMarker NewMarker(LocalMapPCView map, PointerEventData eventData)
        {
            string areaName = Game.Instance.CurrentlyLoadedArea.AreaDisplayName;
            List <ModMapMarker> markersForArea;
            if (!AreaMarkers.TryGetValue(areaName, out markersForArea)) { AreaMarkers[areaName] = new List<ModMapMarker>(); }
            Vector3 position = GetPositionFromEvent(map, eventData);
            ModMapMarker marker = new ModMapMarker(position);
            AreaMarkers[areaName].Add(marker);
            return marker;
        }

        private static Vector3 GetPositionFromEvent(LocalMapPCView map, PointerEventData eventData)
        {
#if DEBUG
            // Perform extra sanity checks in debug builds.

            Log.Write($"GetPositionFromEvent was reached.");
#endif
            Vector2 vector2;
            RectTransform rect = Helpers.GetField<RawImage>(map, "m_Image").rectTransform;
#if DEBUG
            // Perform extra sanity checks in debug builds.

            Log.Write($"m_Image works.");
#endif
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, Game.Instance.UI.UICamera, out vector2);
            vector2 += Vector2.Scale(rect.sizeDelta, rect.pivot);
            LocalMapVM lmVM = (LocalMapVM) map.GetPropertyValue("ViewModel");
#if DEBUG
            // Perform extra sanity checks in debug builds.

            Log.Write($"ViewModel works too.");
#endif
            LocalMapRenderer.DrawResult drawResult = lmVM.DrawResult.Value;
#if DEBUG
            // Perform extra sanity checks in debug builds.

            Log.Write($"m_DrawResult works too.");
#endif
            Vector2 vector21 = new Vector2(vector2.x / (float)drawResult.ColorRT.width, vector2.y / (float)drawResult.ColorRT.height);
            Vector3 worldPoint = LocalMapRenderer.Instance.ViewportToWorldPoint(vector21);
            return worldPoint;
        }

        internal static void AddMarkerstoLocalMap()
        {
            if (StateManager.CurrentState.IsLocalMapInitialized) { return; }

#if DEBUG
            foreach (var marker in LocalMapModel.Markers)
            {
                if (marker is ModMapMarker)
                {
                    Log.Write($"AddMarkerstoLocalMap: ModMapMarker present before add! marker=[{((ModMapMarker)marker).Description}]");
                    // LocalMap.Markers.Remove(marker);
                }
            }
#endif

            string areaName = Game.Instance.CurrentlyLoadedArea.AreaDisplayName;
            List<ModMapMarker> markers;
            if (AreaMarkers.TryGetValue(areaName, out markers))
            {
                foreach (var marker in markers)
                {
                    Log.Write($"AddMarkerstoLocalMap: marker=[{marker.Description}]");
                    LocalMapModel.Markers.Add(marker);
                }
            }

            StateManager.CurrentState.IsLocalMapInitialized = true;
        }

        internal static void RemoveMarkersFromLocalMap()
        {
            string areaName = Game.Instance.CurrentlyLoadedArea.AreaDisplayName;
            List<ModMapMarker> markers;
            if (AreaMarkers.TryGetValue(areaName, out markers))
            {
                foreach (var marker in markers)
                {
                    Log.Write($"RemoveMarkersFromLocalMap: marker=[{((ModMapMarker)marker).Description}]");
                    LocalMapModel.Markers.Remove(marker);
                }
            }

            StateManager.CurrentState.IsLocalMapInitialized = false;
        }
    }

    [DataContract]
    class ModMapMarker :  ILocalMapMarker
	{
        [DataMember]
        internal string Description;
        [DataMember]
        private SerializableVector3 Position;
        [DataMember]
        internal LocalMapMarkType Type;
        [DataMember]
        internal bool IsVisible = true;

        internal bool IsDeleted = false;
        internal bool IsBeingDeleted = false;

        internal ModMapMarker(Vector3 position)
        {
            Description = $"Custom marker #{StateManager.CurrentState.MarkerNumber++}";
            Position = position;
            Type = LocalMapMarkType.Poi;
        }

        string ILocalMapMarker.GetDescription()
            => Description;

        LocalMapMarkType ILocalMapMarker.GetMarkerType()
            => Type;

        Vector3 ILocalMapMarker.GetPosition()
            => Position;

        bool ILocalMapMarker.IsVisible()
            => IsVisible;

    }

    class CustomMapMarkersMenu {
        private static Dictionary<string, List<ModMapMarker>> AreaMarkers { get { return StateManager.CurrentState.AreaMarkers; } }
        private static int lastAreaMenu = 0;
        private static string[] MarkTypeNames = { "Point of Interest", "Very Important Thing", "Loot", "Exit" };
        private static LocalMapMarkType[] MarkTypes = { LocalMapMarkType.Poi, LocalMapMarkType.VeryImportantThing, LocalMapMarkType.Loot, LocalMapMarkType.Exit };


        internal static void Layout()
        {
            var fixedWidth = new GUILayoutOption[1] { GUILayout.ExpandWidth(false) };
            if (AreaMarkers.Count == 0)
            {
                GUILayout.Label("<b>No custom markers.</b>", fixedWidth);
                return;
            }

            string[] areaNames = AreaMarkers.Keys.ToArray();
            Array.Sort(areaNames);
            lastAreaMenu = (lastAreaMenu >= areaNames.Length) ? 0 : lastAreaMenu;

            GUILayout.Label("<b><color=cyan>Select area</color></b>", fixedWidth);
            lastAreaMenu = GUILayout.SelectionGrid(lastAreaMenu, areaNames, 5, fixedWidth);
            GUILayout.Space(10f);
            GUILayout.Label($"<b><color=cyan>{areaNames[lastAreaMenu]}</color></b>", fixedWidth);
            LayoutMarkersForArea(areaNames[lastAreaMenu]);
        }

        private static void LayoutMarkersForArea(string areaName)
        {
            var fixedWidth = new GUILayoutOption[1] { GUILayout.ExpandWidth(false) };

            uint markerNumber = 1;
            foreach (var marker in AreaMarkers[areaName])
            {
                if (marker.IsDeleted) { continue; }

                GUILayout.Space(10f);

                string markerLabel = $"{markerNumber++}: {marker.Description}";
                if (marker.IsVisible) { markerLabel = $"<color=#1aff1a><b>{markerLabel}</b></color>"; }
                GUILayout.Label(markerLabel, fixedWidth);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Description: ", fixedWidth);
                marker.Description = GUILayout.TextField(marker.Description, GUILayout.MaxWidth(500f));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Type: ", fixedWidth);
                for (int i = 0; i < MarkTypeNames.Length; i++)
                {
                    if (GUILayout.Toggle(marker.Type == MarkTypes[i], MarkTypeNames[i], fixedWidth))
                    {
                        marker.Type = MarkTypes[i];
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(marker.IsVisible ? "Hide" : "Show", fixedWidth))
                {
                    marker.IsVisible = !marker.IsVisible;
                }
                if (!marker.IsBeingDeleted && GUILayout.Button("Delete", fixedWidth))
                {
                    marker.IsBeingDeleted = true;
                }
                if (marker.IsBeingDeleted)
                {
                    GUILayout.Label("Are you sure?", fixedWidth);
                    if (GUILayout.Button("Yes", fixedWidth))
                    {
                        LocalMapModel.Markers.Remove(marker);
                        marker.IsDeleted = true;
                        marker.IsVisible = false;
                    }
                    if (GUILayout.Button("No", fixedWidth))
                    {
                        marker.IsBeingDeleted = false;
                    }
                }
                GUILayout.EndHorizontal();
            } 
        }
    }
}
