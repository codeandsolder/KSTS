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

namespace KSTS
{

    public enum TemplateOrigin { VAB, SPH, Subassemblies };

    // Helper class to store a ships template (from the craft's save-file) together with its generated thumbnail:
    public class CachedShipTemplate
    {
        public string vesselName;
        public DateTime lastWriteTime;
        public ShipTemplate template = null;
        public Texture2D thumbnail = null;
        public bool placeholderThumbnail = true;
        public TemplateOrigin templateOrigin;

        private int? cachedCrewCapacity = null;
        private double? cachedDryMass = null;

        // Returns a list of all the parts (as part-definitions) of the given template:
        public static List<AvailablePart> GetTemplateParts(ShipTemplate template)
        {
            var parts = new List<AvailablePart>();
            if (template?.config == null) throw new Exception("invalid template");
            foreach (var node in template.config.GetNodes())
            {
                if (node.name.ToLower() != "part") continue; // There are no other nodes-types in the vessel-config, but lets be safe.
                if (!node.HasValue("part")) continue;
                var partName = node.GetValue("part");
                partName = Regex.Replace(partName, "_[0-9A-Fa-f]+$", ""); // The name of the part is appended by the UID (eg "Mark2Cockpit_4294755350"), which is numeric, but it won't hurt if we also remove hex-characters here.
                if (!KSTS.partDictionary.ContainsKey(partName)) { Debug.LogError("part '" + partName + "' not found in global part-directory"); continue; }
                parts.Add(KSTS.partDictionary[partName]);
            }
            return parts;
        }

        public int GetCrewCapacity()
        {
            if (cachedCrewCapacity != null) return (int)cachedCrewCapacity;
            var crewCapacity = 0;
            if (HighLogic.LoadedScene == GameScenes.FLIGHT) throw new Exception("it is not safe to run this function while in flight"); // This applies to "ShipConstruction.LoadShip()", but I haven't tested "ShipConstruction.LoadSubassembly()" but lets be safe here.
            try
            {
                /*
                 * Originally we used "ShipConstruction.LoadShip()" to load the vessel's construct which contained all initialized objects
                 * for its parts. In the flight-scene this created new, non-functioning vessels next to the active vessel. It did work however
                 * in the space center, which is why we didn't allow this function to get called from the flight-scene. In any case apparently
                 * a "ShipConstruct" object can't exist on its own, because the original implementation threw a continuous stream of exceptions
                 * outside of our own code, which is why we use the following, cumbersome metod to try and parse the saved ship.
                 */
                if (template == null) throw new Exception("missing template");
                foreach (var availablePart in GetTemplateParts(template))
                {
                    if (availablePart.partConfig.HasValue("CrewCapacity"))
                    {
                        var parsedCapacity = 0;
                        if (int.TryParse(availablePart.partConfig.GetValue("CrewCapacity"), out parsedCapacity)) crewCapacity += parsedCapacity;
                    }
                }

                cachedCrewCapacity = crewCapacity;
            }
            catch (Exception e)
            {
                Debug.LogError("CachedShipTemplate::GetCrewCapacity(): " + e.ToString());
            }
            return crewCapacity;
        }

        public double GetDryMass()
        {
            if (cachedDryMass != null) return (double)cachedDryMass;
            double dryMass = 0;
            if (HighLogic.LoadedScene == GameScenes.FLIGHT) throw new Exception("ShipConstruction.LoadShip cannot be run while in flight"); // See "GetCrewCapacity".
            try
            {
                foreach (var availablePart in GetTemplateParts(template))
                {
                    // Get the part's mass (should be the dry-mass, the resources are extra):
                    if (availablePart.partConfig.HasValue("mass"))
                    {
                        double parsedMass = 0;
                        if (Double.TryParse(availablePart.partConfig.GetValue("mass"), out parsedMass)) dryMass += parsedMass;
                    }
                }

                cachedDryMass = dryMass;
            }
            catch (Exception e)
            {
                Debug.LogError("CachedShipTemplate::GetDryMass(): " + e.ToString());
            }
            return dryMass;
        }
    }
}
