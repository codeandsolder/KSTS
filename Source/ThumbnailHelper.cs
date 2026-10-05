using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KSTS
{
    public class ThumbnailHelper
    {
        // Builds a render-only representation of the craft. This deliberately never instantiates
        // Part, PartModule, ShipConstruct, Vessel, or a part/model prefab GameObject. Only Transform,
        // MeshFilter, MeshRenderer, SkinnedMeshRenderer and private Material objects are created.
        internal static bool CaptureThumbnail(CraftAnalysis analysis, int resolution, string fullFilePath, bool isVab)
        {
            List<Material> ownedMaterials = new List<Material>();
            GameObject renderRoot = BuildCraftRenderTree(analysis, ownedMaterials);
            if (renderRoot == null) return false;

            try
            {
                CaptureThumbnailFromGameObject(renderRoot, resolution, fullFilePath, isVab);
                return File.Exists(fullFilePath);
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] Failed to render craft thumbnail: " + e);
                return false;
            }
            finally
            {
                if (renderRoot != null) UnityEngine.Object.DestroyImmediate(renderRoot);
                for (int i = ownedMaterials.Count - 1; i >= 0; i--)
                {
                    if (ownedMaterials[i] != null) UnityEngine.Object.DestroyImmediate(ownedMaterials[i]);
                }
            }
        }

        private static GameObject BuildCraftRenderTree(CraftAnalysis analysis, List<Material> ownedMaterials)
        {
            if (analysis == null) return null;

            GameObject craftRoot = new GameObject("KSTS_ThumbnailRoot");
            craftRoot.SetActive(false);
            try
            {
                Dictionary<uint, GameObject> modelsByCraftId = new Dictionary<uint, GameObject>();
                Dictionary<uint, List<uint>> childLinksByCraftId = new Dictionary<uint, List<uint>>();

                for (int i = 0; i < analysis.parts.Count; i++)
                {
                    CraftPartAnalysis part = analysis.parts[i];
                    if (!part.hasRenderPlacement || part.availablePart == null || part.availablePart.partPrefab == null) continue;

                    GameObject partModel = BuildPartRenderTree(part, ownedMaterials);
                    if (partModel == null) continue;

                    partModel.name = part.partName + "_" + part.craftIdString;
                    partModel.transform.position = part.position;
                    partModel.transform.rotation = part.rotation;
                    partModel.transform.SetParent(craftRoot.transform, true);
                    partModel.SetActive(true); // parent craftRoot is still inactive
                    modelsByCraftId[part.craftId] = partModel;

                    if (part.childIds.Count > 0)
                        childLinksByCraftId[part.craftId] = part.childIds;
                }

                foreach (KeyValuePair<uint, List<uint>> links in childLinksByCraftId)
                {
                    GameObject parentModel;
                    if (!modelsByCraftId.TryGetValue(links.Key, out parentModel)) continue;
                    for (int i = 0; i < links.Value.Count; i++)
                    {
                        GameObject childModel;
                        if (!modelsByCraftId.TryGetValue(links.Value[i], out childModel)) continue;
                        childModel.transform.SetParent(parentModel.transform, true);
                    }
                }

                if (modelsByCraftId.Count == 0)
                {
                    UnityEngine.Object.DestroyImmediate(craftRoot);
                    return null;
                }
                return craftRoot;
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] Error building render-only craft model: " + e);
                UnityEngine.Object.DestroyImmediate(craftRoot);
                return null;
            }
        }

        private static GameObject BuildPartRenderTree(CraftPartAnalysis part, List<Material> ownedMaterials)
        {
            AvailablePart availablePart = part.availablePart;
            Transform sourceModel = FindPartModelTransform(availablePart.partPrefab.transform);
            if (sourceModel == null) return null;

            // Keep an inert wrapper disabled for the entire build. The cloned visual subtree can
            // preserve all saved activeSelf flags without becoming activeInHierarchy prematurely.
            GameObject partRoot = new GameObject("KSTS_PartRenderRoot");
            partRoot.SetActive(false);
            Dictionary<Transform, Transform> transformMap = new Dictionary<Transform, Transform>();
            GameObject renderModel = CloneTransformTree(sourceModel, partRoot.transform, transformMap);
            CopyRenderers(sourceModel, transformMap, ownedMaterials);

            // Reset variant-controlled visual state to the prefab's base variant first, then apply
            // the saved variant. PartVariant.UpdateModel only toggles GameObjects; material changes
            // are copied directly below. No ModulePartVariants method is invoked, so no KSP events
            // or module lifecycle callbacks can fire.
            PartVariant baseVariant = availablePart.partPrefab.baseVariant;
            if (baseVariant != null) ApplyVariantVisuals(renderModel, baseVariant);
            PartVariant savedVariant = part.selectedVariant;
            if (savedVariant != null && savedVariant != baseVariant) ApplyVariantVisuals(renderModel, savedVariant);

            float tweakScale = part.tweakScaleModifier;
            if (Math.Abs(tweakScale - 1.0f) > 0.000001f)
                partRoot.transform.localScale = new Vector3(tweakScale, tweakScale, tweakScale);

            return partRoot;
        }

        private static Transform FindPartModelTransform(Transform partRoot)
        {
            Transform found = FindNamedTransform(partRoot, "model");
            if (found == null) found = FindNamedTransform(partRoot, "model01");
            if (found == null) found = FindNamedTransform(partRoot, "Asteroid");
            return found ?? partRoot;
        }

        private static Transform FindNamedTransform(Transform root, string name)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == name) return transforms[i];
            }
            return null;
        }

        private static GameObject CloneTransformTree(Transform source, Transform parent, Dictionary<Transform, Transform> transformMap)
        {
            GameObject clone = new GameObject(source.name);
            clone.SetActive(false);
            clone.transform.SetParent(parent, false);
            clone.transform.localPosition = source.localPosition;
            clone.transform.localRotation = source.localRotation;
            clone.transform.localScale = source.localScale;
            transformMap[source] = clone.transform;

            for (int i = 0; i < source.childCount; i++)
            {
                GameObject childClone = CloneTransformTree(source.GetChild(i), clone.transform, transformMap);
                childClone.SetActive(source.GetChild(i).gameObject.activeSelf);
            }
            clone.SetActive(source.gameObject.activeSelf);
            return clone;
        }

        private static void CopyRenderers(Transform sourceRoot, Dictionary<Transform, Transform> transformMap, List<Material> ownedMaterials)
        {
            Transform[] sourceTransforms = sourceRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < sourceTransforms.Length; i++)
            {
                Transform source = sourceTransforms[i];
                Transform destination;
                if (!transformMap.TryGetValue(source, out destination)) continue;

                MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
                MeshRenderer sourceMeshRenderer = source.GetComponent<MeshRenderer>();
                if (sourceFilter != null && sourceMeshRenderer != null)
                {
                    MeshFilter destinationFilter = destination.gameObject.AddComponent<MeshFilter>();
                    destinationFilter.sharedMesh = sourceFilter.sharedMesh;
                    MeshRenderer destinationRenderer = destination.gameObject.AddComponent<MeshRenderer>();
                    destinationRenderer.enabled = sourceMeshRenderer.enabled;
                    destinationRenderer.sharedMaterials = CloneMaterials(sourceMeshRenderer.sharedMaterials, ownedMaterials);
                }

                SkinnedMeshRenderer sourceSkinned = source.GetComponent<SkinnedMeshRenderer>();
                if (sourceSkinned != null)
                {
                    SkinnedMeshRenderer destinationSkinned = destination.gameObject.AddComponent<SkinnedMeshRenderer>();
                    destinationSkinned.enabled = sourceSkinned.enabled;
                    destinationSkinned.sharedMesh = sourceSkinned.sharedMesh;
                    destinationSkinned.localBounds = sourceSkinned.localBounds;
                    destinationSkinned.updateWhenOffscreen = sourceSkinned.updateWhenOffscreen;
                    destinationSkinned.sharedMaterials = CloneMaterials(sourceSkinned.sharedMaterials, ownedMaterials);

                    Transform mappedRootBone;
                    if (sourceSkinned.rootBone != null && transformMap.TryGetValue(sourceSkinned.rootBone, out mappedRootBone))
                        destinationSkinned.rootBone = mappedRootBone;

                    Transform[] sourceBones = sourceSkinned.bones;
                    Transform[] destinationBones = new Transform[sourceBones.Length];
                    for (int boneIndex = 0; boneIndex < sourceBones.Length; boneIndex++)
                    {
                        Transform mappedBone;
                        if (sourceBones[boneIndex] != null && transformMap.TryGetValue(sourceBones[boneIndex], out mappedBone))
                            destinationBones[boneIndex] = mappedBone;
                    }
                    destinationSkinned.bones = destinationBones;
                }
            }
        }

        private static Material[] CloneMaterials(Material[] sourceMaterials, List<Material> ownedMaterials)
        {
            Material[] result = new Material[sourceMaterials.Length];
            for (int i = 0; i < sourceMaterials.Length; i++)
            {
                if (sourceMaterials[i] == null) continue;
                Material material = new Material(sourceMaterials[i]);
                material.name = sourceMaterials[i].name;
                result[i] = material;
                ownedMaterials.Add(material);
            }
            return result;
        }

        private static void ApplyVariantVisuals(GameObject renderModel, PartVariant variant)
        {
            if (renderModel == null || variant == null) return;
            variant.UpdateModel(renderModel.transform);
            if (variant.Materials == null || variant.Materials.Count == 0) return;

            Renderer[] renderers = renderModel.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Material[] materials = renderers[rendererIndex].sharedMaterials;
                for (int variantIndex = 0; variantIndex < variant.Materials.Count; variantIndex++)
                {
                    Material variantMaterial = variant.Materials[variantIndex];
                    if (variantMaterial == null) continue;
                    for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    {
                        Material material = materials[materialIndex];
                        if (material == null || !material.name.StartsWith(variantMaterial.name, StringComparison.Ordinal)) continue;
                        material.shader = variantMaterial.shader;
                        material.CopyPropertiesFromMaterial(variantMaterial);
                    }
                }
                renderers[rendererIndex].sharedMaterials = materials;
            }
        }

        private static void CaptureThumbnailFromGameObject(GameObject root, int resolution, string fullFilePath, bool isVab)
        {
            int renderLayer = LayerMask.NameToLayer("UIAdditional");
            if (renderLayer < 0) renderLayer = 0;
            SetLayerRecursive(root, renderLayer);
            root.SetActive(true);

            Bounds bounds;
            if (!TryGetRendererBounds(root, out bounds)) return;

            const float cameraFov = 30f;
            float elevation = isVab ? 45f : 35f;
            float azimuth = isVab ? 45f : 135f;
            float pitch = isVab ? 45f : 35f;
            float heading = isVab ? 45f : 135f;
            float cameraDistance = KSPCameraUtil.GetDistanceToFit(bounds.size, cameraFov * 0.9f);

            GameObject cameraObject = new GameObject("KSTS_SnapshotCamera");
            Texture2D texture = null;
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.Color;
                camera.backgroundColor = Color.clear;
                camera.fieldOfView = cameraFov;
                camera.cullingMask = 1 << renderLayer;
                camera.enabled = false;
                camera.allowHDR = false;

                Light light = cameraObject.AddComponent<Light>();
                light.type = LightType.Spot;
                light.range = 100f;
                light.intensity = 0.5f;
                light.cullingMask = 1 << renderLayer;

                camera.transform.position = bounds.center +
                    Quaternion.AngleAxis(azimuth, Vector3.up) *
                    Quaternion.AngleAxis(elevation, Vector3.right) *
                    (Vector3.back * cameraDistance);
                camera.transform.rotation = Quaternion.AngleAxis(heading, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.right);

                texture = RenderCamera(camera, resolution, resolution, 24);
                string directory = Path.GetDirectoryName(fullFilePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllBytes(fullFilePath, texture.EncodeToPNG());
            }
            finally
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                root.SetActive(false);
            }
        }

        private static Texture2D RenderCamera(Camera camera, int width, int height, int depth)
        {
            RenderTexture renderTexture = new RenderTexture(width, height, depth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            renderTexture.Create();
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = renderTexture;
                camera.targetTexture = renderTexture;
                camera.Render();
                Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                texture.Apply();
                return texture;
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
        }

        private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            bounds = new Bounds(root.transform.position, Vector3.zero);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null || !renderers[i].enabled || !renderers[i].gameObject.activeInHierarchy) continue;
                if (!found)
                {
                    bounds = renderers[i].bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }
            return found;
        }

        private static void SetLayerRecursive(GameObject root, int layer)
        {
            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
                SetLayerRecursive(root.transform.GetChild(i).gameObject, layer);
        }
    }
}
