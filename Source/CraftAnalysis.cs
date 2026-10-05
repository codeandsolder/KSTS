using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KSTS
{
    internal sealed class CraftPartAnalysis
    {
        public ConfigNode node;
        public AvailablePart availablePart;
        public string partName;
        public string craftIdString;
        public uint craftId;
        public bool hasRenderPlacement;
        public Vector3 position;
        public Quaternion rotation;
        public readonly List<uint> childIds = new List<uint>();
        public PartVariant selectedVariant;
        public float tweakScaleModifier = 1.0f;
        public ConfigNode tweakScaleNode;
        public readonly List<ConfigNode> interstellarFuelSwitchNodes = new List<ConfigNode>();
    }

    internal sealed class CraftAnalysis
    {
        public readonly List<CraftPartAnalysis> parts = new List<CraftPartAnalysis>();
        public bool allPartsResolved = true;
        public int crewCapacity;
        public double dryMass;

        public static CraftAnalysis Analyze(ConfigNode craftConfig)
        {
            var analysis = new CraftAnalysis();
            if (craftConfig == null)
            {
                analysis.allPartsResolved = false;
                return analysis;
            }

            ConfigNode[] partNodes = craftConfig.GetNodes("PART");
            if (partNodes == null || partNodes.Length == 0)
            {
                analysis.allPartsResolved = false;
                return analysis;
            }

            for (int i = 0; i < partNodes.Length; i++)
            {
                ConfigNode partNode = partNodes[i];
                AvailablePart availablePart = ResolveAvailablePart(partNode);
                if (availablePart == null || availablePart.partPrefab == null)
                {
                    analysis.allPartsResolved = false;
                    continue;
                }

                CraftPartAnalysis part = AnalyzePart(partNode, availablePart);
                analysis.parts.Add(part);
                analysis.crewCapacity += GetPartCrewCapacity(part);

                try
                {
                    float dryCost;
                    float fuelCost;
                    float partDryMass;
                    float fuelMass;
                    ShipConstruction.GetPartCostsAndMass(
                        partNode,
                        availablePart,
                        out dryCost,
                        out fuelCost,
                        out partDryMass,
                        out fuelMass);
                    analysis.dryMass += partDryMass;
                }
                catch (Exception e)
                {
                    Debug.LogError("[KSTS] Failed to calculate dry mass for " + availablePart.name + ": " + e);
                }
            }

            return analysis;
        }

        private static AvailablePart ResolveAvailablePart(ConfigNode partNode)
        {
            if (partNode == null || !partNode.HasValue("part")) return null;
            try
            {
                return PartLoader.getPartInfoByName(ShipTemplate.GetPartName(partNode));
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] Invalid PART node: " + e);
                return null;
            }
        }

        private static CraftPartAnalysis AnalyzePart(ConfigNode partNode, AvailablePart availablePart)
        {
            var result = new CraftPartAnalysis
            {
                node = partNode,
                availablePart = availablePart,
                partName = availablePart.name
            };

            string selectedVariantName = null;
            bool hasVariantsModule = false;
            ConfigNode[] moduleNodes = partNode.GetNodes("MODULE");
            for (int i = 0; i < moduleNodes.Length; i++)
            {
                ConfigNode moduleNode = moduleNodes[i];
                string moduleName = moduleNode.GetValue("name");
                if (moduleName == "ModulePartVariants" && !hasVariantsModule)
                {
                    hasVariantsModule = true;
                    selectedVariantName = moduleNode.GetValue("selectedVariant");
                }
                else if ((moduleName == "TweakScale" || moduleName == "ModuleTweakScale") && result.tweakScaleNode == null)
                {
                    result.tweakScaleNode = moduleNode;
                }
                else if (moduleName == "InterstellarFuelSwitch")
                {
                    result.interstellarFuelSwitchNodes.Add(moduleNode);
                }
            }

            List<PartVariant> variants = availablePart.Variants;
            if (variants != null && variants.Count > 0)
            {
                PartVariant fallbackVariant = availablePart.partPrefab.baseVariant ?? variants[0];
                if (!hasVariantsModule)
                {
                    result.selectedVariant = fallbackVariant;
                }
                else if (selectedVariantName == null)
                {
                    result.selectedVariant = variants[0];
                }
                else
                {
                    result.selectedVariant = fallbackVariant;
                    for (int i = 0; i < variants.Count; i++)
                    {
                        if (variants[i].Name != selectedVariantName) continue;
                        result.selectedVariant = variants[i];
                        break;
                    }
                }
            }

            if (result.tweakScaleNode != null)
            {
                bool active = true;
                bool savedActive;
                if (result.tweakScaleNode.HasValue("active") &&
                    bool.TryParse(result.tweakScaleNode.GetValue("active"), out savedActive))
                {
                    active = savedActive;
                }

                double defaultScale;
                double currentScale;
                if (active &&
                    TryParseInvariantDouble(result.tweakScaleNode.GetValue("defaultScale"), out defaultScale) &&
                    TryParseInvariantDouble(result.tweakScaleNode.GetValue("currentScale"), out currentScale) &&
                    Math.Abs(defaultScale) >= double.Epsilon)
                {
                    result.tweakScaleModifier = (float)(currentScale / defaultScale);
                }
            }

            if (partNode.HasValue("part") && partNode.HasValue("pos") && partNode.HasValue("rot"))
            {
                string partName = string.Empty;
                string craftIdString = string.Empty;
                KSPUtil.GetPartInfo(partNode.GetValue("part"), ref partName, ref craftIdString);
                uint craftId;
                if (!string.IsNullOrEmpty(partName) && uint.TryParse(craftIdString, out craftId))
                {
                    result.partName = partName;
                    result.craftIdString = craftIdString;
                    result.craftId = craftId;
                    result.position = KSPUtil.ParseVector3(partNode.GetValue("pos"));
                    result.rotation = KSPUtil.ParseQuaternion(partNode.GetValue("rot"));
                    result.hasRenderPlacement = true;

                    string[] links = partNode.GetValues("link");
                    if (links != null)
                    {
                        for (int linkIndex = 0; linkIndex < links.Length; linkIndex++)
                        {
                            uint childId;
                            if (uint.TryParse(KSPUtil.GetLinkID(links[linkIndex]), out childId))
                                result.childIds.Add(childId);
                        }
                    }
                }
            }

            return result;
        }

        private static int GetPartCrewCapacity(CraftPartAnalysis part)
        {
            int prefabCapacity = part.availablePart.partPrefab.CrewCapacity;

            int switchedCapacity;
            if (TryGetInterstellarFuelSwitchCrewCapacity(part, out switchedCapacity))
                return switchedCapacity;

            ConfigNode tweakScaleNode = part.tweakScaleNode;
            if (tweakScaleNode == null) return prefabCapacity;

            int originalCapacity = prefabCapacity;
            int savedOriginalCapacity;
            if (tweakScaleNode.HasValue("OriginalCrewCapacity") &&
                int.TryParse(tweakScaleNode.GetValue("OriginalCrewCapacity"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out savedOriginalCapacity))
            {
                originalCapacity = savedOriginalCapacity;
            }

            bool active = true;
            bool savedActive;
            if (tweakScaleNode.HasValue("active") && bool.TryParse(tweakScaleNode.GetValue("active"), out savedActive))
                active = savedActive;
            if (!active) return originalCapacity;

            double defaultScale;
            double currentScale;
            if (!TryParseInvariantDouble(tweakScaleNode.GetValue("defaultScale"), out defaultScale) ||
                !TryParseInvariantDouble(tweakScaleNode.GetValue("currentScale"), out currentScale) ||
                Math.Abs(defaultScale) < double.Epsilon)
            {
                return originalCapacity;
            }

            double scale = currentScale / defaultScale;
            if (Math.Abs(scale - 1.0) <= 1e-5) return originalCapacity;

            int scaledCapacity = (int)Math.Round(originalCapacity * Math.Pow(scale, GetTweakScaleCrewCapacityExponent()));
            return Math.Max(0, Math.Min(prefabCapacity, scaledCapacity));
        }

        private static bool TryGetInterstellarFuelSwitchCrewCapacity(CraftPartAnalysis part, out int crewCapacity)
        {
            crewCapacity = 0;
            if (part.availablePart.partConfig == null || part.interstellarFuelSwitchNodes.Count == 0) return false;

            ConfigNode[] prefabModules = part.availablePart.partConfig.GetNodes("MODULE");
            for (int occurrence = 0; occurrence < part.interstellarFuelSwitchNodes.Count; occurrence++)
            {
                ConfigNode prefabModule = null;
                int seen = 0;
                for (int j = 0; j < prefabModules.Length; j++)
                {
                    if (prefabModules[j].GetValue("name") != "InterstellarFuelSwitch") continue;
                    if (seen++ == occurrence)
                    {
                        prefabModule = prefabModules[j];
                        break;
                    }
                }
                if (prefabModule == null) continue;

                bool controlsCrewCapacity;
                if (!bool.TryParse(prefabModule.GetValue("controlCrewCapacity"), out controlsCrewCapacity) ||
                    !controlsCrewCapacity)
                {
                    continue;
                }

                ConfigNode savedModule = part.interstellarFuelSwitchNodes[occurrence];
                int selectedTankSetup;
                if (!int.TryParse(savedModule.GetValue("selectedTankSetup"), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out selectedTankSetup) || selectedTankSetup < 0)
                {
                    return false;
                }

                double tankCapacity;
                if (!TryGetSemicolonDouble(prefabModule.GetValue("crewCapacity"), selectedTankSetup, out tankCapacity))
                    tankCapacity = 0.0;

                double surfaceMultiplier = 1.0;
                double savedSurfaceMultiplier;
                if (TryParseInvariantDouble(savedModule.GetValue("storedSurfaceMultiplier"), out savedSurfaceMultiplier))
                    surfaceMultiplier = savedSurfaceMultiplier;

                crewCapacity = (int)Math.Round(tankCapacity * surfaceMultiplier);
                return true;
            }
            return false;
        }

        private static bool TryGetSemicolonDouble(string values, int index, out double result)
        {
            result = 0.0;
            if (string.IsNullOrEmpty(values) || index < 0) return false;

            string[] items = values.Trim().Split(';');
            int parsedIndex = 0;
            for (int i = 0; i < items.Length; i++)
            {
                double parsed;
                if (!TryParseInvariantDouble(items[i].Trim(), out parsed)) continue;
                if (parsedIndex++ != index) continue;
                result = parsed;
                return true;
            }
            return false;
        }

        private static double GetTweakScaleCrewCapacityExponent()
        {
            const double defaultExponent = 2.0;
            try
            {
                if (GameDatabase.Instance == null) return defaultExponent;
                var configs = GameDatabase.Instance.GetConfigs("TWEAKSCALEEXPONENTS");
                if (configs == null) return defaultExponent;

                for (int i = 0; i < configs.Length; i++)
                {
                    ConfigNode config = configs[i].config;
                    if (config == null) continue;
                    string name = config.GetValue("name");
                    if (string.IsNullOrEmpty(name)) name = "Part";
                    if (name != "Part" || !config.HasValue("CrewCapacity")) continue;

                    double exponent;
                    if (TryParseInvariantDouble(config.GetValue("CrewCapacity"), out exponent)) return exponent;
                    return defaultExponent;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KSTS] Failed to read TweakScale CrewCapacity exponent: " + e);
            }
            return defaultExponent;
        }

        private static bool TryParseInvariantDouble(string value, out double result)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }
    }
}
