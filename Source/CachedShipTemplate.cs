using System;
using System.IO;
using UnityEngine;

namespace KSTS
{
    public enum TemplateOrigin { VAB, SPH, SubAssembly };

    // Cached metadata for one craft file. The object deliberately is not a
    // MonoBehaviour: it has no Unity lifecycle and is keyed by the craft path.
    public class CachedShipTemplate
    {
        public ShipTemplate template = null;
        public Texture2D thumbnail = null;
        public TemplateOrigin templateOrigin;
        public string craftFilePath = null;

        private DateTime craftLastWriteTimeUtc = DateTime.MinValue;
        private long craftFileLength = -1;
        private string thumbnailFilePath = null;
        private DateTime thumbnailLastWriteTimeUtc = DateTime.MinValue;
        private long thumbnailFileLength = -1;
        private bool thumbnailOutdated = true;
        private CraftAnalysis analysis = new CraftAnalysis();

        public static CachedShipTemplate Load(string craftFilePath, TemplateOrigin origin)
        {
            var fileInfo = new FileInfo(craftFilePath);
            var loadedTemplate = ShipConstruction.LoadTemplate(fileInfo.FullName);
            if (loadedTemplate == null) return null;

            return new CachedShipTemplate
            {
                template = loadedTemplate,
                templateOrigin = origin,
                craftFilePath = fileInfo.FullName,
                craftLastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                craftFileLength = fileInfo.Length,
                thumbnail = GUI.placeholderImage,
                analysis = CraftAnalysis.Analyze(loadedTemplate.config)
            };
        }

        public bool MatchesCraftFile(FileInfo fileInfo)
        {
            return fileInfo != null &&
                   craftLastWriteTimeUtc == fileInfo.LastWriteTimeUtc &&
                   craftFileLength == fileInfo.Length;
        }

        public void RefreshMissionAvailability()
        {
            if (HighLogic.CurrentGame == null ||
                (HighLogic.CurrentGame.Mode != Game.Modes.MISSION && HighLogic.CurrentGame.Mode != Game.Modes.MISSION_BUILDER))
            {
                return;
            }

            // Mission filters are external state. Refresh only the stock template's
            // availability flags; the craft revision and its analyzed PART data did not change.
            ShipTemplate refreshed = ShipConstruction.LoadTemplate(craftFilePath);
            if (refreshed != null) template = refreshed;
        }

        public bool IsUsable()
        {
            if (template == null || template.config == null || template.duplicatedParts ||
                analysis == null || !analysis.allPartsResolved || analysis.parts.Count == 0)
            {
                return false;
            }

            if (HighLogic.CurrentGame != null &&
                (HighLogic.CurrentGame.Mode == Game.Modes.MISSION || HighLogic.CurrentGame.Mode == Game.Modes.MISSION_BUILDER) &&
                (!template.shipPartsUnlocked || template.shipPartsExperimental))
            {
                return false;
            }

            // R&D availability can change without the craft file changing. Reuse the
            // resolved AvailablePart objects from the revision analysis and only recheck state.
            for (int i = 0; i < analysis.parts.Count; i++)
            {
                AvailablePart availablePart = analysis.parts[i].availablePart;
                if (ResearchAndDevelopment.IsExperimentalPart(availablePart)) return false;
                if (!ResearchAndDevelopment.PartTechAvailable(availablePart)) return false;
            }
            return true;
        }

        public int GetCrewCapacity()
        {
            return analysis != null ? analysis.crewCapacity : 0;
        }

        public double GetDryMass()
        {
            return analysis != null ? analysis.dryMass : 0.0;
        }

        public void RefreshThumbnailFromDisk(string filePath)
        {
            thumbnailFilePath = filePath;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                thumbnailLastWriteTimeUtc = DateTime.MinValue;
                thumbnailFileLength = -1;
                thumbnailOutdated = true;
                ReplaceThumbnail(GUI.placeholderImage);
                return;
            }

            var fileInfo = new FileInfo(filePath);
            if (thumbnail != null && thumbnail != GUI.placeholderImage &&
                thumbnailLastWriteTimeUtc == fileInfo.LastWriteTimeUtc &&
                thumbnailFileLength == fileInfo.Length)
            {
                return;
            }

            Texture2D fullSize = null;
            try
            {
                fullSize = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                fullSize.LoadImage(File.ReadAllBytes(filePath));
                Texture2D resized = GUI.ResizeTexture(fullSize, 64, 64);
                ReplaceThumbnail(resized);
                thumbnailLastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
                thumbnailFileLength = fileInfo.Length;
                thumbnailOutdated = fileInfo.LastWriteTimeUtc < craftLastWriteTimeUtc;
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] Failed to load thumbnail '" + filePath + "': " + e);
                ReplaceThumbnail(GUI.placeholderImage);
            }
            finally
            {
                if (fullSize != null) UnityEngine.Object.Destroy(fullSize);
            }
        }

        public void TryGenerateMissingThumbnail()
        {
            if (!thumbnailOutdated && thumbnail != null && thumbnail != GUI.placeholderImage) return;
            if (template == null || template.config == null || string.IsNullOrEmpty(thumbnailFilePath)) return;

            try
            {
                // Never create a Part/PartModule/ShipConstruct merely for a preview. ThumbnailHelper
                // uses the revision analysis to select prefab visual state and manually copies only
                // inert transforms/rendering components into a detached render tree.
                bool isVab = templateOrigin != TemplateOrigin.SPH;
                if (ThumbnailHelper.CaptureThumbnail(analysis, 256, thumbnailFilePath, isVab))
                    RefreshThumbnailFromDisk(thumbnailFilePath);
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] Failed to generate thumbnail for '" + craftFilePath + "': " + e);
            }
        }

        private void ReplaceThumbnail(Texture2D replacement)
        {
            if (thumbnail != null && thumbnail != GUI.placeholderImage && thumbnail != replacement)
            {
                UnityEngine.Object.Destroy(thumbnail);
            }
            thumbnail = replacement ?? GUI.placeholderImage;
        }

        public void Dispose()
        {
            ReplaceThumbnail(GUI.placeholderImage);
        }
    }
}
