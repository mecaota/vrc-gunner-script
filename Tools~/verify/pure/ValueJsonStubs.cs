// ValueJsonTest 用の小さな身代わり(Unity・UdonSharp の型の名前だけを合わせる)。
// GunnerValueSchema.FromType は属性と型を名前で見るので、同じ名前の型で同じ結果になる。
// ほかのパッケージの検証でも、部品の本物のソースをそのままコンパイルするのに使う。
using System;

namespace UnityEngine
{
    public class Object { }
    public class Component : Object { }
    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }
    public class Transform : Component { }

    [AttributeUsage(AttributeTargets.Field)] public class HeaderAttribute : Attribute { public HeaderAttribute(string header) { } }
    [AttributeUsage(AttributeTargets.Field)] public class HideInInspector : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
    [AttributeUsage(AttributeTargets.Field)] public class MinAttribute : Attribute { public MinAttribute(float min) { } }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public static class Mathf
    {
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
    }
}

namespace UdonSharp
{
    public enum BehaviourSyncMode { Any, None, NoVariableSync, Continuous, Manual }
    [AttributeUsage(AttributeTargets.Class)] public class UdonBehaviourSyncModeAttribute : Attribute { public UdonBehaviourSyncModeAttribute(BehaviourSyncMode mode) { } }
    [AttributeUsage(AttributeTargets.Field)] public class UdonSyncedAttribute : Attribute { }
    public class UdonSharpBehaviour : UnityEngine.MonoBehaviour { }
}

namespace Mecaota.Gunner
{
    // 受け手の基底。この型で宣言した欄は一覧に入れない(FromType の SkipDeclaringTypes)
    public abstract class GunnerHitReceiver : UdonSharp.UdonSharpBehaviour
    {
        public bool wantsLateStep;
    }
}

namespace ValueJsonFakes
{
    using UnityEngine;
    using UdonSharp;

    // 射手に似せた部品: 参照・隠し・同期・実行時・private・文字列が混ざる
    public class FakeShooter : Mecaota.Gunner.GunnerHitReceiver
    {
        public Transform rig;
        public float aimTime = 0.6f;
        public int magazine = 3;
        [Range(0f, 4f)] public int maxCombo = 4;
        public float watchRange = 0f;
        public bool watchOnly = false;
        public string phaseEvent = "_OnPhase";
        [HideInInspector] public int[] baked = new int[0];
        [System.NonSerialized] public int phase;
        [UdonSynced] public int syncedValue;
        private float hidden = 1f;
        [SerializeField] private float serializedPrivate = 2f;
        public const float Constant = 1f;
        public static float StaticValue = 1f;
        public readonly float ReadOnlyValue = 1f;
        public float[] perEmitter = { 10f, 20f, 30f };
        public Vector3 center;
        public Color tint = new Color(1f, 0.5f, 0.25f, 1f);
        public Transform[] safeZones;
        public float Hidden => hidden + serializedPrivate;
    }
}
