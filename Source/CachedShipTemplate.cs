using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ClickThroughFix;
using KSP.Localization;
using KSP.UI.Screens; // For "ApplicationLauncherButton"
using KSTS;
using ToolbarControl_NS;
using UnityEngine;
using KASAPIv2;

namespace KSTS
{

    public enum TemplateOrigin { VAB, SPH, Subassemblies};

    // Helper class to store a ships template (from the craft's save-file) together with its generated thumbnail:
    public class CachedShipTemplate : MonoBehaviour
    {
        public ShipTemplate template = null;
        public Texture2D thumbnail = null;
        public TemplateOrigin templateOrigin;
        public string vesselName = null;
        public DateTime lastWriteTime = DateTime.MinValue;

        private List<AvailablePart> cachedParts = null;

		public int crewCapacity = 0;
        public double dryMass = 0;

        private bool invalidated = false;

		public void IngestNodes()
        {
            if (this.cachedParts != null && this.dryMass > 0) { this.AcquireThumbnail(); return; }
            if (this.template == null || this.template.config == null) { this.invalidated = true; return; }
            this.cachedParts = new List<AvailablePart>();
			this.crewCapacity = 0;
			this.dryMass = 0;

            foreach (ConfigNode node in template.config.GetNodes())
            {
                try
                {
                    if (!string.Equals(node.name, "part", StringComparison.OrdinalIgnoreCase)) { continue; }
                    if (!node.HasValue("part")) { continue; }

                    string partName = node.GetValue("part");
                    partName = partName.Substring(0, partName.LastIndexOf("_"));    //XKCD_208
                    if (!KSTS.partDictionary.TryGetValue(partName, out AvailablePart availablePart))
                    {
                        Debug.LogError("[KSTS] Part '" + partName + "' not found in global part-directory");
                        continue;
                    }
					this.cachedParts.Add(availablePart);

                    if (availablePart.partConfig.HasValue("CrewCapacity"))
                    {
                        int parsedCapacity = 0;
                        if (int.TryParse(availablePart.partConfig.GetValue("CrewCapacity"), out parsedCapacity))
							this.crewCapacity += parsedCapacity;
                    }
                    if (availablePart.partConfig.HasValue("mass"))
                    {
                        double parsedMass = 0;
                        if (Double.TryParse(availablePart.partConfig.GetValue("mass"), out parsedMass))
							this.dryMass += parsedMass;
                        if (Double.TryParse(node.GetValue("modMass"), out parsedMass))
							this.dryMass += parsedMass;
                    }
				}
                catch (Exception e)
                {
                    Debug.LogError("[KSTS] Node indigestion! " + e.ToString());
                }
			}
            if (this.dryMass == 0 || this.cachedParts.Count == 0){ invalidated = true; return; }
		}
		
		internal void AcquireThumbnail()
		{
			if(this.thumbnail != null && this.thumbnail != GUI.placeholderImage) { return; }
			string thumbnailFilePath = ThumbnailHelper.GetThumbnailFilePath(this.templateOrigin, this.vesselName);
			Texture2D scratchThumbnail = new Texture2D(2, 2);
			if (File.Exists(thumbnailFilePath))
			{
				scratchThumbnail.LoadImage(File.ReadAllBytes(thumbnailFilePath));
				this.thumbnail = ThumbnailHelper.ResizeTexture(scratchThumbnail, 64, 64);
			}
            else if (HighLogic.LoadedSceneIsEditor)
            {
				
				ShipConstruct strippedShip = ShipConstruction.StripPartComponents(this.template, this.cachedParts);
				//ShipConstruction.CreateConstructFromTemplate(strippedShip;, delegate (ShipConstruct construct)
				//GameObject modelObj = PartUtilsImpl.GetSceneAssemblyModel()
				ThumbnailHelper.CaptureThumbnail(strippedShip, 256, "thumbs/", ThumbnailHelper.GetThumbnailFilename(this.templateOrigin, this.vesselName));
                scratchThumbnail.LoadImage(File.ReadAllBytes(thumbnailFilePath));
                this.thumbnail = ThumbnailHelper.ResizeTexture(scratchThumbnail, 64, 64);
                Debug.Log("[KSTS] Successfully captured thumbnail for " + this.vesselName);
			}
            //else
            //{
            //    this.invalidated = true;
            //    Debug.LogWarning("[KSTS] Ingest first, thumbnail second!");
			//}
		}
	}
}
