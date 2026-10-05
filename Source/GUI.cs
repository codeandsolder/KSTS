using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using KSP.UI.Screens; // For "ApplicationLauncherButton"
using System.Text.RegularExpressions;
using KSP.Localization;

using ToolbarControl_NS;
using ClickThroughFix;
using static KSTS.Statics;

namespace KSTS
{

    // Creates the button and contains the functionality to draw the GUI-window (we want to use the same window
    // for different scenes, that is why we have a few helper-classes above):
    [KSPAddon(KSPAddon.Startup.SpaceCentre, true)]
    public class GUI : UnityEngine.MonoBehaviour
    {
        public const int WIDTH = 450;
        public const int WIDTH_SPACECENTER = WIDTH + (WIDTH / 6 - 6);

        public static Rect windowPosition = new Rect(300, 60, WIDTH, 400);
        //public static GUIStyle windowStyle;
        public static bool showGui = false;

        // Styles (initialized in OnReady):
        public static GUIStyle labelStyle = null;
        public static GUIStyle buttonStyle = null;
        public static Color normalGUIbackground;
        public static GUIStyle textFieldStyle = null;
        public static GUIStyle scrollStyle = null;
        public static GUIStyle selectionGridStyle = null;

        // Common resources:
        //private static ApplicationLauncherButton button = null;
        ToolbarControl toolbarControl = null;
        internal const string MODID = "KSTS_NS";
        internal const string MODNAME = "Kerbal Space Transport System";

        //private static Texture2D buttonIcon = null;
        private static int selectedMainTab = 0;
        public static Texture2D placeholderImage = null;
        public static List<CachedShipTemplate> shipTemplates = null;

        private static readonly StringComparer craftPathComparer =
            Environment.OSVersion.Platform == PlatformID.Win32NT ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        private static readonly Dictionary<string, CachedShipTemplate> shipTemplateCache =
            new Dictionary<string, CachedShipTemplate>(craftPathComparer);

        private static string helpText = "";
        private static Vector2 helpTabScrollPos = Vector2.zero;

        void Awake()
        {
#if false
            if (windowStyle == null)
            {
                windowStyle = new GUIStyle(HighLogic.Skin.window) { fixedWidth = 450f, fixedHeight = 500f };
            }
            if (buttonIcon == null)
            {
                buttonIcon = new Texture2D(36, 36, TextureFormat.RGBA32, false);
                buttonIcon.LoadImage(File.ReadAllBytes(
                    Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "../PluginData/KSTS_icon.png"))
                    );
            }
#endif
            if (placeholderImage == null)
            {
                placeholderImage = new Texture2D(275, 275, TextureFormat.RGBA32, false);
                placeholderImage.LoadImage(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "../PluginData/placeholder.png")));
                placeholderImage = GUI.ResizeTexture(placeholderImage, 64, 64); // Default-size for our ship-icons
            }

            if (helpText == "")
            {
                var helpFilename = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/../PluginData/help.txt";
                if (File.Exists(helpFilename)) helpText = File.ReadAllText(helpFilename);
                else
                {
                    helpText = "Help-file not found.";
                    Log.Warning("helpFilename: " + helpFilename);
                }

            }

            DontDestroyOnLoad(this);
        }

        void Start()
        {
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle("Label");
                buttonStyle = new GUIStyle("Button");
                normalGUIbackground = UnityEngine.GUI.backgroundColor;
                textFieldStyle = new GUIStyle("TextField");
                scrollStyle = HighLogic.Skin.scrollView;
                selectionGridStyle = new GUIStyle(GUI.buttonStyle) { richText = true, fontStyle = FontStyle.Normal, alignment = TextAnchor.UpperLeft };
            }
            InitializeButton();
        }

        void InitializeButton()
        {
            if (toolbarControl != null)
                return;

            toolbarControl = gameObject.AddComponent<ToolbarControl>();
            toolbarControl.AddToAllToolbars(GuiOn, GuiOff,
                ApplicationLauncher.AppScenes.SPACECENTER |
                ApplicationLauncher.AppScenes.TRACKSTATION |
                ApplicationLauncher.AppScenes.FLIGHT,
                MODID,
                "KSTSButton",
                "KSTS/PluginData/KSTS_icon_38",
                "KSTS/PluginData/KSTS_icon_24",
                MODNAME
            );

        }

        void NoOnDestroy()
        {
            toolbarControl.OnDestroy();
            Destroy(toolbarControl);
            toolbarControl = null;
            showGui = false;
        }

        private void GuiOn()
        {
            Log.Warning("KSTS: GuiOn");
            showGui = true;
            if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
            {
                         windowPosition = new Rect(windowPosition.x, windowPosition.y, WIDTH_SPACECENTER, 400);
            }
            else
            {
                windowPosition = new Rect(windowPosition.x, windowPosition.y, WIDTH, 400);
            }
            GUI.UpdateShipTemplateCache(true);
        }

        private void GuiOff()
        {
            showGui = false;
        }

        public static string FormatDuration(double duration)
        {
            var dayLength = 24;
            if (GameSettings.KERBIN_TIME) dayLength = 6;
            var seconds = duration % 60;
            var minutes = ((int)(duration / 60)) % 60;
            var hours = ((int)(duration / 60 / 60)) % dayLength;
            var days = ((int)(duration / 60 / 60 / dayLength));
            return String.Format("{0:0}/{1:00}:{2:00}:{3:00.00}", days, hours, minutes, seconds);
        }

        public static string FormatAltitude(double altitude)
        {
            if (altitude >= 1000000) return (altitude / 1000).ToString("#,##0km");
            else return altitude.ToString("#,##0m");
        }

        // Returns a thumbnail for a given vessel-name (used to find fitting images for vessels used in mission-profiles):
        public static Texture2D GetVesselThumbnail(string vesselName)
        {
            foreach (var cachedTemplate in GUI.shipTemplates)
            {
                // This is strictly not correct, because the player could name VAB and SPH vessels the same, but this is easier
                // than to also save the editor-type in the mission-profile:
                if (Localizer.Format(cachedTemplate.template.shipName) == vesselName) return cachedTemplate.thumbnail;
            }
            return GUI.placeholderImage; // Fallback
        }

        // For some reason Unity has no resize method, so we have to implement our own:
        public static Texture2D ResizeTexture(Texture2D input, int width, int height)
        {
            var small = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var rx = (float)input.width / (float)small.width;
            var ry = (float)input.height / (float)small.height;
            for (var y = 0; y < small.height; y++)
            {
                var sy = (int)Math.Round(ry * y);
                for (var x = 0; x < small.width; x++)
                {
                    var sx = (int)Math.Round(rx * x);
                    small.SetPixel(x, y, input.GetPixel(sx, sy));
                }
            }
            small.Apply();
            return small;
        }

        static string[] editorFacilities = { "VAB", "SPH" }; // This is usually an enum, but we need the string later.

        // Updates the cache we use to store the meta-data of the various ships the player has designed.
        // Unchanged craft keep their parsed ShipTemplate and thumbnail. We only reload files whose
        // timestamp/length changed, add new files, and evict files that disappeared.
        public static void UpdateShipTemplateCache(bool generateMissingThumbnails = false)
        {
            Log.Warning("KSTS: UpdateShipTemplateCache");

            if (GUI.shipTemplates == null) GUI.shipTemplates = new List<CachedShipTemplate>();
            var seenCraftFiles = new HashSet<string>(craftPathComparer);

            foreach (var editorFacility in editorFacilities)
            {
                TemplateOrigin origin = editorFacility == "SPH" ? TemplateOrigin.SPH : TemplateOrigin.VAB;
                string shipDirectory = KSPUtil.ApplicationRootPath + "/saves/" + HighLogic.SaveFolder + "/Ships/" + editorFacility;
                ReadAllCraftFiles(origin, editorFacility, shipDirectory, seenCraftFiles);
            }

            string subassemblyDirectory = KSPUtil.ApplicationRootPath + "/saves/" + HighLogic.SaveFolder + "/Subassemblies";
            ReadAllCraftFiles(TemplateOrigin.SubAssembly, "Subassemblies", subassemblyDirectory, seenCraftFiles);

            var stalePaths = new List<string>();
            foreach (var item in shipTemplateCache)
            {
                if (!seenCraftFiles.Contains(item.Key)) stalePaths.Add(item.Key);
            }
            foreach (string stalePath in stalePaths)
            {
                shipTemplateCache[stalePath].Dispose();
                shipTemplateCache.Remove(stalePath);
            }

            GUI.shipTemplates.Clear();
            foreach (CachedShipTemplate cachedTemplate in shipTemplateCache.Values)
            {
                cachedTemplate.RefreshMissionAvailability();
                if (cachedTemplate.IsUsable()) GUI.shipTemplates.Add(cachedTemplate);
            }

            GUI.shipTemplates.Sort(delegate(CachedShipTemplate x, CachedShipTemplate y)
            {
                int byName = string.Compare(x.template.shipName, y.template.shipName, StringComparison.CurrentCulture);
                if (byName != 0) return byName;
                return craftPathComparer.Compare(x.craftFilePath, y.craftFilePath);
            });

            // The thumbnail path is render-only and never activates craft/module lifecycle.
            // Generate only stale/missing thumbnails when explicitly requested, in any scene.
            if (generateMissingThumbnails)
            {
                foreach (CachedShipTemplate cachedTemplate in GUI.shipTemplates)
                {
                    cachedTemplate.TryGenerateMissingThumbnail();
                }
            }
        }

        static void ReadAllCraftFiles(TemplateOrigin origin, string facilityName, string shipDirectory, HashSet<string> seenCraftFiles)
        {
            string normalizedShipDirectory = Path.GetFullPath(shipDirectory);
            if (!Directory.Exists(normalizedShipDirectory)) return;

            foreach (string craftFile in Directory.EnumerateFiles(normalizedShipDirectory, "*.craft", SearchOption.AllDirectories))
            {
                try
                {
                    string validFileName = Path.GetFileNameWithoutExtension(craftFile);
                    if (string.IsNullOrEmpty(validFileName) || validFileName == "Auto-Saved Ship") continue;

                    var fileInfo = new FileInfo(craftFile);
                    string fullPath = fileInfo.FullName;
                    seenCraftFiles.Add(fullPath);

                    CachedShipTemplate cachedTemplate;
                    if (!shipTemplateCache.TryGetValue(fullPath, out cachedTemplate) || !cachedTemplate.MatchesCraftFile(fileInfo))
                    {
                        if (cachedTemplate != null) cachedTemplate.Dispose();
                        shipTemplateCache.Remove(fullPath);

                        cachedTemplate = CachedShipTemplate.Load(fullPath, origin);
                        if (cachedTemplate == null) continue;
                        shipTemplateCache[fullPath] = cachedTemplate;
                    }

                    cachedTemplate.RefreshThumbnailFromDisk(GetThumbnailFilePath(facilityName, normalizedShipDirectory, fullPath));
                }
                catch (Exception e)
                {
                    Debug.LogError("UpdateShipTemplateCache() processing '" + craftFile + "': " + e);
                }
            }
        }

        static string GetThumbnailFilePath(string facilityName, string shipDirectory, string craftFile)
        {
            string normalizedShipDirectory = Path.GetFullPath(shipDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedCraftFile = Path.GetFullPath(craftFile);
            string relativePath = normalizedCraftFile.Substring(normalizedShipDirectory.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string relativeDirectory = Path.GetDirectoryName(relativePath);

            string subDirectoryPath = "/" + facilityName;
            if (!string.IsNullOrEmpty(relativeDirectory))
            {
                subDirectoryPath += "/" + relativeDirectory.Replace(Path.DirectorySeparatorChar, '/');
            }

            // Mirrors ShipConstruction.GetPlayerCraftThumbnailName without reloading the
            // craft just to rediscover its facility.
            string thumbName = KSPUtil.SanitizeFilename(HighLogic.SaveFolder) +
                KSPUtil.SanitizeFilename(subDirectoryPath) + "_" +
                KSPUtil.SanitizeString(Path.GetFileNameWithoutExtension(craftFile), '_', true);

            return Path.Combine(KSPUtil.ApplicationRootPath, "thumbs", thumbName + ".png");
        }

        // Synchronizes the craft cache and resets UI state, for example when a savegame is loaded:
        public static void Reset()
        {
            UpdateShipTemplateCache();
            GUIStartDeployMissionTab.Reset();
            GUIStartTransportMissionTab.Reset();
            GUIRecordingTab.Reset();
        }

        // Moved here to avoid reinitializing every single loop
        static string[] toolbarStrings = new string[] { "Flights", "Deploy", "Transport", "Construct", "Record", "Help", "Settings" };

        // Is called by our helper-classes to draw the actual window:
        public static void DrawWindow()
        {
            if (!showGui) return;
            try
            {
                GUILayout.BeginVertical();

                // Title:

                GUILayout.BeginArea(new Rect(0, 3,windowPosition.width /* windowStyle.fixedWidth */, 20));
                GUILayout.Label("<size=14><b>Kerbal Space Transport System</b></size>", new GUIStyle(GUI.labelStyle) { fixedWidth = windowPosition.width /*  windowStyle.fixedWidth */, alignment = TextAnchor.MiddleCenter });
                GUILayout.EndArea();

                // Tab-Switcher:
                if (MissionController.missionProfiles.Count == 0)
                {
                    int x = WIDTH / 6 - 6;
                    GUILayout.BeginHorizontal();
                    UnityEngine.GUI.enabled = false;
                    for (int i = 0; i < 4; i++)
                        GUILayout.Button(toolbarStrings[i], GUILayout.Width(x));
                    UnityEngine.GUI.enabled = true;
                    if (GUILayout.Button(toolbarStrings[4], GUILayout.Width(x)))
                        selectedMainTab = 4;
                    if (GUILayout.Button(toolbarStrings[5], GUILayout.Width(x)))
                        selectedMainTab = 5;
                    if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
                    {
                        if (GUILayout.Button(toolbarStrings[6], GUILayout.Width(x)))
                            selectedMainTab = 6;
                    }
                    GUILayout.EndHorizontal();
                }
                else
                    selectedMainTab = GUILayout.Toolbar(selectedMainTab, toolbarStrings);

                switch (selectedMainTab)
                {
                    // Flights:
                    case 0:
                        GUIFlightsTab.Display();
                        break;

                    // Deploy:
                    case 1:
                        if (HighLogic.LoadedScene == GameScenes.FLIGHT)
                        {
                            GUILayout.BeginScrollView(Vector2.zero, GUI.scrollStyle);
                            GUILayout.Label("<b>Please go to the Space Center to launch a new mission.</b>");
                            GUILayout.EndScrollView();
                        }
                        else if (GUIStartDeployMissionTab.Display()) selectedMainTab = 0;
                        break;

                    // Transport:
                    case 2:
                        if (HighLogic.LoadedScene == GameScenes.FLIGHT)
                        {
                            GUILayout.BeginScrollView(Vector2.zero, GUI.scrollStyle);
                            GUILayout.Label("<b>Please go to the Space Center to launch a new mission.</b>");
                            GUILayout.EndScrollView();
                        }
                        else if (GUIStartTransportMissionTab.Display()) selectedMainTab = 0;
                        break;

                    // Construct:
                    case 3:
                        if (HighLogic.LoadedScene == GameScenes.FLIGHT)
                        {
                            GUILayout.BeginScrollView(Vector2.zero, GUI.scrollStyle);
                            GUILayout.Label("<b>Please go to the Space Center to launch a new mission.</b>");
                            GUILayout.EndScrollView();
                        }
                        else if (GUIStartConstructMissionTab.Display()) selectedMainTab = 0;
                        break;

                    // Record:
                    case 4:
                        GUIRecordingTab.Display();
                        break;

                    // Help:
                    case 5:
                        helpTabScrollPos = GUILayout.BeginScrollView(helpTabScrollPos, GUI.scrollStyle);
                        GUILayout.Label(helpText);
                        GUILayout.EndScrollView();
                        break;

                    case 6:                        
                       
                        GUILayout.Label("<size=14><b>Alarm Clock Settings:</b></size>");
                        GUILayout.BeginScrollView(new Vector2(0,0), GUI.scrollStyle);

                        MissionController.useKACifAvailable = GUILayout.Toggle(MissionController.useKACifAvailable, "Use Kerbal Alarm Clock (if available)");
                        MissionController.useStockAlarmClock = GUILayout.Toggle(MissionController.useStockAlarmClock, "Use Stock Alarm Clock");

                        GUILayout.EndScrollView();

                        break;

                    default:
                        GUILayout.Label("<b>Not implemented yet.</b>");
                        break;
                }

                GUILayout.EndVertical();
                UnityEngine.GUI.DragWindow();
            }
            catch (Exception e)
            {
                Debug.LogError("DrawWindow(): " + e.ToString());
            }
        }
    }
}
