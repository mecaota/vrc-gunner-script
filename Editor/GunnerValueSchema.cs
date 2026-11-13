using System;
using System.Collections.Generic;
using System.Reflection;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>JSON に出し入れできる欄の型。参照(オブジェクト・Transform・音・マテリアル)、曲線、レイヤーは扱わない。</summary>
    public enum GunnerValueKind { Int, Float, Bool, String, Vector2, Vector3, Vector4, Color, IntArray, FloatArray, BoolArray }

    /// <summary>
    /// JSON に出し入れする1つの欄。名前は Unity のシリアライズ名(欄の名前)そのもの。
    /// 範囲: hardMin / hardMax の外は赤(読み込まない)、softMin / softMax の外は丸めて黄。配列は要素ごとに同じ範囲。
    /// </summary>
    public sealed class GunnerValueField
    {
        public readonly string name;
        public readonly GunnerValueKind kind;
        public double hardMin = double.NegativeInfinity, hardMax = double.PositiveInfinity;
        public double softMin = double.NegativeInfinity, softMax = double.PositiveInfinity;
        public int length = -1;          // 配列: 決まった長さ(-1 = 決めない)
        public bool keepLength = true;   // 配列: 長さが決まっていない時、今の長さと違えば赤(ほかの配列と番号が対になっている欄を壊さない)
        public int maxLength = 1024;     // 配列: keepLength が偽の時の長さの上限

        public GunnerValueField(string name, GunnerValueKind kind)
        {
            this.name = name;
            this.kind = kind;
        }

        public bool IsArray => kind == GunnerValueKind.IntArray || kind == GunnerValueKind.FloatArray || kind == GunnerValueKind.BoolArray;
        public bool IsNumber => kind == GunnerValueKind.Int || kind == GunnerValueKind.Float || kind == GunnerValueKind.IntArray || kind == GunnerValueKind.FloatArray;
        public bool IsInt => kind == GunnerValueKind.Int || kind == GunnerValueKind.IntArray;

        /// <summary>ベクトル・色の成分の数。ほかは 0。</summary>
        public int Components
        {
            get
            {
                switch (kind)
                {
                    case GunnerValueKind.Vector2: return 2;
                    case GunnerValueKind.Vector3: return 3;
                    case GunnerValueKind.Vector4:
                    case GunnerValueKind.Color: return 4;
                    default: return 0;
                }
            }
        }

        public GunnerValueField Clone()
        {
            return new GunnerValueField(name, kind)
            {
                hardMin = hardMin, hardMax = hardMax, softMin = softMin, softMax = softMax,
                length = length, keepLength = keepLength, maxLength = maxLength,
            };
        }
    }

    /// <summary>
    /// 1つの部品の型について、JSON に出し入れする欄の一覧(許可リスト)。JSON の "type" には typeName を書く。
    /// <see cref="FromType"/> で型の欄から作り、使う側が除外・範囲・長さを足す。
    /// </summary>
    public sealed class GunnerValueSchema
    {
        public readonly string typeName;
        readonly List<GunnerValueField> fields = new List<GunnerValueField>();

        public GunnerValueSchema(string typeName) { this.typeName = typeName ?? ""; }

        public IReadOnlyList<GunnerValueField> Fields => fields;

        public GunnerValueField Find(string name)
        {
            foreach (var f in fields) if (f.name == name) return f;
            return null;
        }

        public GunnerValueSchema Add(string name, GunnerValueKind kind)
        {
            if (string.IsNullOrEmpty(name) || Find(name) != null) return this;
            fields.Add(new GunnerValueField(name, kind));
            return this;
        }

        /// <summary>欄を一覧から外す(JSON に出さず、JSON にあれば「知らないキー」として飛ばす)。</summary>
        public GunnerValueSchema Exclude(params string[] names)
        {
            foreach (var n in names) fields.RemoveAll(f => f.name == n);
            return this;
        }

        /// <summary>数の欄を負にできなくする(負は赤)。名前を渡さなければ、数の欄すべて(ベクトル・色は含めない)。</summary>
        public GunnerValueSchema NonNegative(params string[] names)
        {
            foreach (var f in Pick(names)) if (f.IsNumber) f.hardMin = Math.Max(f.hardMin, 0);
            return this;
        }

        /// <summary>挙げた欄を除く、数の欄すべてを負にできなくする。</summary>
        public GunnerValueSchema NonNegativeExcept(params string[] names)
        {
            foreach (var f in fields)
                if (f.IsNumber && Array.IndexOf(names, f.name) < 0) f.hardMin = Math.Max(f.hardMin, 0);
            return this;
        }

        /// <summary>この範囲の外は丸めて黄。</summary>
        public GunnerValueSchema Range(string name, double min, double max)
        {
            var f = Find(name);
            if (f != null) { f.softMin = min; f.softMax = max; }
            return this;
        }

        /// <summary>この範囲の外は赤(読み込まない)。</summary>
        public GunnerValueSchema HardRange(string name, double min, double max)
        {
            var f = Find(name);
            if (f != null) { f.hardMin = min; f.hardMax = max; }
            return this;
        }

        /// <summary>配列の長さを決める(違えば赤)。</summary>
        public GunnerValueSchema Length(string name, int length)
        {
            var f = Find(name);
            if (f != null && f.IsArray) f.length = length;
            return this;
        }

        /// <summary>配列の長さを自由にする(上限まで)。ほかの配列と番号が対になっていない欄だけに使う。</summary>
        public GunnerValueSchema AnyLength(string name, int maxLength)
        {
            var f = Find(name);
            if (f != null && f.IsArray) { f.keepLength = false; f.maxLength = maxLength; }
            return this;
        }

        public GunnerValueSchema Copy(string newTypeName = null)
        {
            var s = new GunnerValueSchema(newTypeName ?? typeName);
            foreach (var f in fields) s.fields.Add(f.Clone());
            return s;
        }

        IEnumerable<GunnerValueField> Pick(string[] names)
        {
            if (names == null || names.Length == 0)
            {
                foreach (var f in fields) yield return f;
                yield break;
            }
            foreach (var n in names)
            {
                var f = Find(n);
                if (f != null) yield return f;
            }
        }

        // ---------------- 型から作る ----------------

        // ここで止める基底の型(この型と、その先の基底の欄は見ない)
        static readonly string[] StopTypes =
        {
            "UdonSharp.UdonSharpBehaviour", "UnityEngine.MonoBehaviour", "UnityEngine.Behaviour",
            "UnityEngine.Component", "UnityEngine.Object", "System.Object",
        };

        // 欄を持っていても見ない基底の型(受け手の内部の設定)
        static readonly string[] SkipDeclaringTypes = { "Mecaota.Gunner.GunnerHitReceiver" };

        /// <summary>
        /// 型の欄から一覧を作る。入れるのは、Unity が保存する欄(public で NonSerialized でない、または [SerializeField])のうち、
        /// [HideInInspector](焼いた表など)と [UdonSynced](実行時の状態)を除いた、int・float・bool・Vector2/3/4・Color と
        /// int[]・float[]・bool[]。文字列は includeStrings の時だけ(GUID や名前の控えを書き換えないため)。
        /// 基底の欄が先、宣言の順。[Range] は丸めの範囲に、[Min] は丸めの下限にする。属性は名前で見る(Unity の型に依存しない)。
        /// </summary>
        public static GunnerValueSchema FromType(Type type, bool includeStrings = false)
        {
            var schema = new GunnerValueSchema(type != null ? type.Name : "");
            if (type == null) return schema;
            var chain = new List<Type>();
            for (var t = type; t != null && Array.IndexOf(StopTypes, t.FullName) < 0; t = t.BaseType) chain.Insert(0, t);
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var t in chain)
            {
                if (Array.IndexOf(SkipDeclaringTypes, t.FullName) >= 0) continue;
                foreach (var f in t.GetFields(Flags))
                {
                    if (f.IsStatic || f.IsInitOnly || f.IsLiteral || ((int)f.Attributes & NotSerializedFlag) != 0) continue;
                    bool serialized = f.IsPublic || HasAttribute(f, "SerializeField");
                    if (!serialized) continue;
                    if (HasAttribute(f, "HideInInspector") || HasAttribute(f, "UdonSyncedAttribute") || HasAttribute(f, "NonSerializedAttribute")) continue;
                    if (!KindOf(f.FieldType, includeStrings, out GunnerValueKind kind)) continue;
                    schema.Add(f.Name, kind);
                    var field = schema.Find(f.Name);
                    ReadRangeAttributes(f, field);
                }
            }
            return schema;
        }

        // [System.NonSerialized] の印(FieldAttributes.NotSerialized = 0x80)。新しい .NET ではその名前が古い扱いで警告になるので数で見る
        const int NotSerializedFlag = 0x80;

        static bool KindOf(Type t, bool includeStrings, out GunnerValueKind kind)
        {
            kind = GunnerValueKind.Int;
            switch (t.FullName)
            {
                case "System.Int32": kind = GunnerValueKind.Int; return true;
                case "System.Single": kind = GunnerValueKind.Float; return true;
                case "System.Boolean": kind = GunnerValueKind.Bool; return true;
                case "System.String": kind = GunnerValueKind.String; return includeStrings;
                case "UnityEngine.Vector2": kind = GunnerValueKind.Vector2; return true;
                case "UnityEngine.Vector3": kind = GunnerValueKind.Vector3; return true;
                case "UnityEngine.Vector4": kind = GunnerValueKind.Vector4; return true;
                case "UnityEngine.Color": kind = GunnerValueKind.Color; return true;
                case "System.Int32[]": kind = GunnerValueKind.IntArray; return true;
                case "System.Single[]": kind = GunnerValueKind.FloatArray; return true;
                case "System.Boolean[]": kind = GunnerValueKind.BoolArray; return true;
            }
            return false;
        }

        static bool HasAttribute(FieldInfo f, string name)
        {
            foreach (var a in f.CustomAttributes) if (a.AttributeType.Name == name) return true;
            return false;
        }

        static void ReadRangeAttributes(FieldInfo f, GunnerValueField field)
        {
            if (field == null || !field.IsNumber) return;
            foreach (var a in f.CustomAttributes)
            {
                var args = a.ConstructorArguments;
                if (a.AttributeType.Name == "RangeAttribute" && args.Count >= 2)
                {
                    field.softMin = Convert.ToDouble(args[0].Value);
                    field.softMax = Convert.ToDouble(args[1].Value);
                }
                else if (a.AttributeType.Name == "MinAttribute" && args.Count >= 1)
                {
                    field.softMin = Convert.ToDouble(args[0].Value);
                }
            }
        }
    }
}
