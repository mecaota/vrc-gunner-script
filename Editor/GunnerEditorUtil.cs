using System;
using System.Collections.Generic;
using System.Reflection;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;
using Object = UnityEngine.Object;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>インスペクタ・点検・セットアップのメニューで共有する小さな補助。</summary>
    public static class GunnerEditorUtil
    {
        /// <summary>
        /// U# 部品をプロキシ経由で編集した後: プロキシ → backing の UdonBehaviour へ写し、プレハブのオーバーライドを記録する。
        /// 逆向きには写さない。編集モードの backing は古い既定値を持っていることがあり、プロキシを消してしまうため。
        /// </summary>
        public static void CommitProxy(UdonSharpBehaviour proxy)
        {
            if (proxy == null) return;
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            if (backing != null)
            {
                Undo.RecordObject(backing, "Gunner");
                // 配列は先に裏側へ直接書く。こうすると CopyProxyToUdon は裏側の項目(書いた複製の配列)を写す先にするので、
                // プログラムの既定のヒープの配列(GetRealProgram のキャッシュ)へは写さない
                SyncBackingArrays(proxy, backing);
                UdonSharpEditorUtility.CopyProxyToUdon(proxy, ProxySerializationPolicy.RootOnly);
                PrefabUtility.RecordPrefabInstancePropertyModifications(backing);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(proxy);
            if (proxy.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(proxy.gameObject.scene);
        }

        // 裏側へ直接書く値の配列の型(裏側の変数の型が C# の型と同じもの)
        static readonly Type[] ValueArrayTypes =
        {
            typeof(int[]), typeof(float[]), typeof(bool[]), typeof(string[]),
            typeof(Vector2[]), typeof(Vector3[]), typeof(Vector4[]), typeof(Color[]),
        };

        /// <summary>
        /// プロキシの値の配列の欄(int・float・bool・string・ベクトル・色の配列)を、裏側の UdonBehaviour の変数と突き合わせ、
        /// 裏側に変数が無い・中身が違う欄だけを裏側へ直接書く。書いた欄の数を返す。
        /// 理由: U# の CopyProxyToUdon(ArraySerializer.Write)は、写す先の配列と長さが同じ時、その配列へ中身を写すだけで
        /// 裏側の変数を書き直さない。裏側に変数が無い時の写す先は、プログラムの既定の値(ヒープ)の配列なので、裏側には何も残らない
        /// (長さが違う・null の時は新しい配列を作って書くので残る)。実行時に効くのは裏側なので、ここで確実に書く。
        /// CopyProxyToUdon より前に呼ぶ(前に呼べば、写す先が裏側の項目になり、既定のヒープの配列を書き換えない)。
        /// 裏側からプロキシへは写さない(UpdateProxy・CopyUdonToProxy は使わない)。Undo は呼ぶ側の RecordObject に入る。
        /// </summary>
        public static int SyncBackingArrays(UdonSharpBehaviour proxy, UdonBehaviour backing)
        {
            if (proxy == null || backing == null || backing.publicVariables == null) return 0;
            var vars = backing.publicVariables;
            int written = 0;
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = proxy.GetType(); t != null && t != typeof(UdonSharpBehaviour); t = t.BaseType)
            {
                foreach (var f in t.GetFields(Flags))
                {
                    if (Array.IndexOf(ValueArrayTypes, f.FieldType) < 0) continue;
                    // [System.NonSerialized] の印は 0x80(保存しない欄は裏側にも写らない)
                    if (f.IsStatic || f.IsInitOnly || ((int)f.Attributes & 0x80) != 0) continue;
                    if (!f.IsPublic && f.GetCustomAttribute<SerializeField>() == null) continue;
                    var value = f.GetValue(proxy) as Array;
                    if (value == null) continue;   // null は CopyProxyToUdon が書く
                    if (vars.TryGetVariableValue(f.Name, out object now) && SameArray(now as Array, value)) continue;
                    var copy = (Array)value.Clone();
                    if (!vars.TrySetVariableValue(f.Name, copy))
                    {
                        var variable = (IUdonVariable)Activator.CreateInstance(typeof(UdonVariable<>).MakeGenericType(f.FieldType), f.Name, copy);
                        if (!vars.TryAddVariable(variable) && !(vars.RemoveVariable(f.Name) && vars.TryAddVariable(variable)))
                        {
                            Debug.LogWarning("[Gunner] could not write the array '" + f.Name + "' to the UdonBehaviour", backing);
                            continue;
                        }
                    }
                    written++;
                }
            }
            if (written > 0) EditorUtility.SetDirty(backing);
            return written;
        }

        static bool SameArray(Array a, Array b)
        {
            if (a == null || b == null || a.GetType() != b.GetType() || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!Equals(a.GetValue(i), b.GetValue(i))) return false;
            return true;
        }

        /// <summary>U# 部品を SerializedObject で編集し、プロキシを確定する。</summary>
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

        /// <summary>親の子でまだ使われていない名前: base, base_1, base_2 ...</summary>
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

        // ---------------- シリアライズされた配列 ----------------

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

        /// <summary>null の要素を除く。並行配列(同じ番号が同じ意味の配列)からも同じ番号を除く。</summary>
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
            // DeleteArrayElementAtIndex は null でないオブジェクトの枠だと1回目は中身を消すだけなので、先に消して1回で除く。
            var e = array.GetArrayElementAtIndex(index);
            if (e.propertyType == SerializedPropertyType.ObjectReference) e.objectReferenceValue = null;
            array.DeleteArrayElementAtIndex(index);
        }

        // ---------------- 粒子 ----------------

        /// <summary>射出口の下の素朴な弾の粒子: World 空間、1発に1粒、短いトレイル。</summary>
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
            main.startLifetime = 1f;      // 寿命は rig が1発ごとに距離から決める
            main.startSpeed = 25f;        // 速さは rig が shotSpeed から決める
            main.startSize = 0.1f;
            main.gravityModifier = 0f;
            main.maxParticles = 64;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            var shape = ps.shape;
            shape.enabled = false;        // +Z へまっすぐ

            var trails = ps.trails;
            trails.enabled = true;
            trails.lifetime = 0.3f;
            trails.minVertexDistance = 0.02f;
            trails.sizeAffectsWidth = false;
            trails.widthOverTrail = 0.08f;   // メートル。はっきり見える線

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

        // ---------------- シーン ----------------

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
