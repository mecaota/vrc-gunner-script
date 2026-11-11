using System.Collections.Generic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>Small helpers shared by the inspectors, checks and setup menus.</summary>
    public static class GunnerEditorUtil
    {
        /// <summary>
        /// After editing a U# component through its proxy: copy proxy -> backing UdonBehaviour and record prefab overrides.
        /// Never copies the other way, because an edit-mode backing can hold stale defaults that would wipe the proxy.
        /// </summary>
        public static void CommitProxy(UdonSharpBehaviour proxy)
        {
            if (proxy == null) return;
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            if (backing != null)
            {
                Undo.RecordObject(backing, "Gunner");
                UdonSharpEditorUtility.CopyProxyToUdon(proxy, ProxySerializationPolicy.RootOnly);
                PrefabUtility.RecordPrefabInstancePropertyModifications(backing);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(proxy);
            if (proxy.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(proxy.gameObject.scene);
        }

        /// <summary>Edits a U# component through a SerializedObject, then commits the proxy.</summary>
        public static void Edit(UdonSharpBehaviour target, System.Action<SerializedObject> change)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            so.Update();
            change(so);
            so.ApplyModifiedProperties();
            CommitProxy(target);
        }

        public static T AddUdon<T>(GameObject go) where T : UdonSharpBehaviour
        {
            return UdonSharpUndo.AddComponent<T>(go);
        }

        public static T GetOrAddUdon<T>(GameObject go) where T : UdonSharpBehaviour
        {
            var found = go.GetComponent<T>();
            return found != null ? found : AddUdon<T>(go);
        }

        public static GameObject CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Gunner: " + name);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
                go.layer = parent.gameObject.layer;
            }
            return go;
        }

        /// <summary>A name not used by the parent's children yet: base, base_1, base_2 ...</summary>
        public static string UniqueChildName(Transform parent, string baseName)
        {
            if (parent == null || parent.Find(baseName) == null) return baseName;
            for (int i = 1; i < 1000; i++)
            {
                string name = baseName + "_" + i;
                if (parent.Find(name) == null) return name;
            }
            return baseName;
        }

        // ---------------- Serialized arrays ----------------

        public static void SetLength(SerializedProperty array, int length)
        {
            if (array.arraySize < length) array.arraySize = length;
        }

        public static void Append(SerializedProperty array, Object value)
        {
            int i = array.arraySize;
            array.arraySize = i + 1;
            array.GetArrayElementAtIndex(i).objectReferenceValue = value;
        }

        public static void SetAt(SerializedProperty array, int index, Object value)
        {
            SetLength(array, index + 1);
            array.GetArrayElementAtIndex(index).objectReferenceValue = value;
        }

        public static bool Contains(SerializedProperty array, Object value)
        {
            for (int i = 0; i < array.arraySize; i++)
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == value) return true;
            return false;
        }

        /// <summary>Removes null entries. Parallel arrays (same index meaning) lose the same indices.</summary>
        public static void RemoveNulls(SerializedObject so, string array, params string[] parallel)
        {
            var main = so.FindProperty(array);
            for (int i = main.arraySize - 1; i >= 0; i--)
            {
                if (main.GetArrayElementAtIndex(i).objectReferenceValue != null) continue;
                RemoveAt(main, i);
                foreach (var name in parallel)
                {
                    var p = so.FindProperty(name);
                    if (p != null && p.isArray && i < p.arraySize) RemoveAt(p, i);
                }
            }
        }

        static void RemoveAt(SerializedProperty array, int index)
        {
            // DeleteArrayElementAtIndex only clears a non-null object slot the first time; clear first so one call removes it.
            var e = array.GetArrayElementAtIndex(index);
            if (e.propertyType == SerializedPropertyType.ObjectReference) e.objectReferenceValue = null;
            array.DeleteArrayElementAtIndex(index);
        }

        // ---------------- Particles ----------------

        /// <summary>A plain shot particle system under the emitter: World space, one particle per shot, a short trail.</summary>
        public static ParticleSystem CreateShotParticles(Transform emitter)
        {
            var go = CreateChild(emitter, "Shot");
            var ps = Undo.AddComponent<ParticleSystem>(go);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.1f;
            main.startLifetime = 1f;      // the rig sets the lifetime per shot from the distance
            main.startSpeed = 25f;        // the rig sets the speed from shotSpeed
            main.startSize = 0.1f;
            main.gravityModifier = 0f;
            main.maxParticles = 64;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            var shape = ps.shape;
            shape.enabled = false;        // straight along +Z

            var trails = ps.trails;
            trails.enabled = true;
            trails.lifetime = 0.3f;
            trails.minVertexDistance = 0.02f;
            trails.sizeAffectsWidth = false;
            trails.widthOverTrail = 0.08f;   // metres: a clearly visible line

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var mat = DefaultParticleMaterial();
            if (renderer != null && mat != null)
            {
                renderer.sharedMaterial = mat;
                renderer.trailMaterial = mat;
            }
            return ps;
        }

        public static Material DefaultParticleMaterial()
        {
            return AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
        }

        public static void MakeWorldSpace(ParticleSystem ps)
        {
            if (ps == null) return;
            Undo.RecordObject(ps, "Gunner: World space");
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            EditorUtility.SetDirty(ps);
        }

        public static void StopAutoPlay(ParticleSystem ps)
        {
            if (ps == null) return;
            Undo.RecordObject(ps, "Gunner: no auto play");
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            EditorUtility.SetDirty(ps);
        }

        public static void EnableTrails(ParticleSystem ps)
        {
            if (ps == null) return;
            Undo.RecordObject(ps, "Gunner: trails");
            var trails = ps.trails;
            trails.enabled = true;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null && renderer.trailMaterial == null)
            {
                Undo.RecordObject(renderer, "Gunner: trails");
                renderer.trailMaterial = renderer.sharedMaterial != null ? renderer.sharedMaterial : DefaultParticleMaterial();
            }
            EditorUtility.SetDirty(ps);
        }

        // ---------------- Scene ----------------

        public static List<T> FindInScenes<T>() where T : Component
        {
            var list = new List<T>();
            foreach (var c in Resources.FindObjectsOfTypeAll<T>())
            {
                if (c == null || EditorUtility.IsPersistent(c)) continue;
                if (c.hideFlags != HideFlags.None) continue;
                if (!c.gameObject.scene.IsValid()) continue;
                list.Add(c);
            }
            return list;
        }

        public static bool InMask(LayerMask mask, int layer) => (mask.value & (1 << layer)) != 0;
    }
}
