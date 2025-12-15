using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KSTS
{
    /////////////////////////////////
    /// Following from:  
    /// http://forum.kerbalspaceprogram.com/threads/119609-Manually-generating-ship-thumbnail
    /// 
    public class ThumbnailHelper
    {
        /// <summary>
        /// Generates a thumbnail exactly like the one KSP generates automatically.
        /// Behaves exactly like ShipConstruction.CaptureThumbnail() but allows customizing the resolution.
        /// If you make a whole ship for it you weill have a lot of unhappy modules.
        /// </summary>
        public static void CaptureThumbnail(ShipConstruct ship, int resolution, string saveFolder, string craftName)
        {
            if (ship.shipFacility != EditorFacility.VAB)
            {
                CraftThumbnail.TakeSnaphot(ship, resolution, saveFolder, craftName, 35, 135, 35, 135, 0.9f);
            }
            else
            {
                CraftThumbnail.TakeSnaphot(ship, resolution, saveFolder, craftName, 45, 45, 45, 45, 0.9f);
            }
        }

        public static void CaptureThumbnail(ShipConstruct ship, int resolution,
                float elevation, float azimuth, float pitch, float heading, float fov, string saveFolder, string craftName)
        {
            CraftThumbnail.TakeSnaphot(ship, resolution, saveFolder, craftName, elevation, azimuth, pitch, heading, fov);
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

        public static string GetThumbnailFilename(TemplateOrigin templateOrigin, string vesselName)
        {
            return HighLogic.SaveFolder + "_" + templateOrigin.ToString() + "_" + vesselName + ".png";
        }
        public static string GetThumbnailFilePath(TemplateOrigin templateOrigin, string vesselName)
        {
            return KSPUtil.ApplicationRootPath + "thumbs/" + GetThumbnailFilename(templateOrigin, vesselName);
        }
    }
}
