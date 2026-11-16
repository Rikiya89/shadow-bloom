using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShadowBloom.EditorTools
{
    /// <summary>
    /// Dumps every visible serialized property of the game hierarchy as text, so two versions of the scene can be
    /// diffed. References print as hierarchy or asset paths (never fileIDs), so turning objects into prefab instances
    /// does not change the output; only real value or wiring changes do.
    /// </summary>
    public static class ShadowBloomAudit
    {
        static readonly string[] Roots = { "ShadowBloom", "Pointable Canvas Module" };

        public static string Dump(string scenePath, string outPath)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var lines = new List<string>();
            foreach (var name in Roots)
            {
                foreach (var root in scene.GetRootGameObjects())
                    if (root.name == name) Walk(root.transform, "", lines);
            }
            var dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(outPath, lines);
            return $"{lines.Count} lines -> {outPath}";
        }

        static void Walk(Transform t, string parent, List<string> lines)
        {
            string path = parent.Length == 0 ? t.name : parent + "/" + t.name;
            var go = t.gameObject;
            lines.Add($"GO {path} active={go.activeSelf} layer={go.layer} tag={go.tag}");
            var counts = new Dictionary<System.Type, int>();
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null)
                {
                    lines.Add("  C MISSING SCRIPT");
                    continue;
                }
                var type = c.GetType();
                counts.TryGetValue(type, out int index);
                counts[type] = index + 1;
                lines.Add($"  C {type.FullName}[{index}]");
                var it = new SerializedObject(c).GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.propertyPath == "m_Script") continue;
                    lines.Add($"    {it.propertyPath} = {Value(it)}");
                }
            }
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), path, lines);
        }

        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        static string Value(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize:
                    return p.longValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
                case SerializedPropertyType.Float: return F(p.floatValue);
                case SerializedPropertyType.String: return "\"" + p.stringValue + "\"";
                case SerializedPropertyType.Enum: return p.enumValueIndex.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Color:
                    var c = p.colorValue;
                    return $"({F(c.r)}, {F(c.g)}, {F(c.b)}, {F(c.a)})";
                case SerializedPropertyType.Vector2: return $"({F(p.vector2Value.x)}, {F(p.vector2Value.y)})";
                case SerializedPropertyType.Vector3:
                    var v3 = p.vector3Value;
                    return $"({F(v3.x)}, {F(v3.y)}, {F(v3.z)})";
                case SerializedPropertyType.Vector4:
                    var v4 = p.vector4Value;
                    return $"({F(v4.x)}, {F(v4.y)}, {F(v4.z)}, {F(v4.w)})";
                case SerializedPropertyType.Quaternion:
                    var q = p.quaternionValue;
                    return $"({F(q.x)}, {F(q.y)}, {F(q.z)}, {F(q.w)})";
                case SerializedPropertyType.Rect:
                    var r = p.rectValue;
                    return $"({F(r.x)}, {F(r.y)}, {F(r.width)}, {F(r.height)})";
                case SerializedPropertyType.ObjectReference:
                case SerializedPropertyType.ExposedReference:
                    return Ref(p.propertyType == SerializedPropertyType.ObjectReference ? p.objectReferenceValue : p.exposedReferenceValue);
                default: return p.propertyType.ToString();
            }
        }

        static string Ref(Object o)
        {
            if (o == null) return "null";
            if (AssetDatabase.Contains(o)) return $"asset:{AssetDatabase.GetAssetPath(o)}:{o.name}:{o.GetType().Name}";
            var t = o is Component c ? c.transform : o is GameObject g ? g.transform : null;
            if (t == null) return $"other:{o.name}:{o.GetType().Name}";
            string path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return $"scene:{path}:{o.GetType().Name}";
        }
    }
}
