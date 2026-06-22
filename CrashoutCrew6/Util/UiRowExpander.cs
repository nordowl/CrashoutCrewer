using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CrashoutCrew6
{
    /// <summary>
    /// Grows a set of parallel "one entry per player" UI reference fields (List&lt;T&gt; or T[])
    /// from their baked size (4) up to a target (6) by cloning existing elements.
    ///
    /// The fields may be organised as independent columns OR as nested rows (where one field's
    /// objects are children of another's). To stay correct in both layouts we never clone a field
    /// in isolation: for each new index we gather the source GameObjects referenced across ALL the
    /// fields, reduce them to their minimal hierarchy "roots", clone each root once, then remap
    /// every field's new reference through the clone hierarchy. This guarantees no duplicate clones.
    /// </summary>
    internal static class UiRowExpander
    {
        private class FieldRef
        {
            public FieldInfo Info;
            public bool IsArray;
            public Type ElementType;
            public IList List;      // live List<T> (when !IsArray)
            public List<object> Work; // working copy of elements (always)
        }

        /// <summary>Expand the given fields on <paramref name="host"/> to <paramref name="target"/> entries.</summary>
        internal static void Expand(Component host, string[] fieldNames, int target, bool squish, float extraScale, string context)
        {
            if (host == null) { Log.Warn($"UiRowExpander[{context}]: host is null."); return; }

            var fields = new List<FieldRef>();
            foreach (var name in fieldNames)
            {
                var fi = AccessTools(host.GetType(), name);
                if (fi == null) { Log.Warn($"UiRowExpander[{context}]: field '{name}' not found."); continue; }
                var val = fi.GetValue(host);
                if (val == null) { Log.Warn($"UiRowExpander[{context}]: field '{name}' is null."); continue; }

                var fr = new FieldRef { Info = fi };
                if (val is Array arr)
                {
                    fr.IsArray = true;
                    fr.ElementType = fi.FieldType.GetElementType();
                    fr.Work = new List<object>();
                    foreach (var o in arr) fr.Work.Add(o);
                }
                else if (val is IList list)
                {
                    fr.IsArray = false;
                    fr.List = list;
                    fr.ElementType = fi.FieldType.IsGenericType ? fi.FieldType.GetGenericArguments()[0] : typeof(object);
                    fr.Work = new List<object>();
                    foreach (var o in list) fr.Work.Add(o);
                }
                else { Log.Warn($"UiRowExpander[{context}]: field '{name}' is neither array nor IList."); continue; }
                fields.Add(fr);
            }

            if (fields.Count == 0) { Log.Warn($"UiRowExpander[{context}]: no usable fields."); return; }

            int origCount = fields[0].Work.Count;
            foreach (var f in fields)
                if (f.Work.Count != origCount)
                    Log.Warn($"UiRowExpander[{context}]: field '{f.Info.Name}' has {f.Work.Count} entries (expected {origCount}); layout may differ.");

            if (origCount >= target)
            {
                Log.Debug($"UiRowExpander[{context}]: already has {origCount} >= {target}, nothing to do.");
                return;
            }
            if (origCount < 2)
            {
                Log.Warn($"UiRowExpander[{context}]: only {origCount} source element(s); cannot derive spacing.");
                return;
            }

            try
            {
                for (int idx = origCount; idx < target; idx++)
                    AddIndex(fields, idx, context);

                if (squish)
                    Squish(fields, origCount, target, extraScale, context);

                // commit working lists back to the real fields
                foreach (var f in fields) Commit(host, f);

                Log.Info($"UiRowExpander[{context}]: expanded {fields.Count} field(s) from {origCount} to {target}.");
            }
            catch (Exception e)
            {
                Log.Error($"UiRowExpander[{context}] failed: {e}");
            }
        }

        private static void AddIndex(List<FieldRef> fields, int idx, string context)
        {
            int src = idx - 1;

            // distinct source GameObjects at index `src`, remembering which field/index each came from
            var goToOrigin = new Dictionary<GameObject, FieldRef>();
            var sourceGOs = new List<GameObject>();
            foreach (var f in fields)
            {
                var go = ToGameObject(f.Work[src]);
                if (go == null) continue;
                if (!goToOrigin.ContainsKey(go)) { goToOrigin[go] = f; sourceGOs.Add(go); }
            }

            // reduce to hierarchy roots (drop any GO that is a descendant of another in the set)
            var roots = new List<GameObject>();
            foreach (var go in sourceGOs)
            {
                bool isDescendant = false;
                foreach (var other in sourceGOs)
                    if (other != go && IsAncestor(other.transform, go.transform)) { isDescendant = true; break; }
                if (!isDescendant) roots.Add(go);
            }

            // clone each root once, mapping original transforms -> clone transforms
            var transformMap = new Dictionary<Transform, Transform>();
            foreach (var root in roots)
            {
                var clone = UnityEngine.Object.Instantiate(root, root.transform.parent, false);
                clone.name = root.name + "_p" + (idx + 1);
                BuildTransformMap(root.transform, clone.transform, transformMap);

                // place the cloned root one row-step past the source row
                var originField = goToOrigin[root];
                var prevGO = ToGameObject(originField.Work[src]);          // == root
                var prevPrevGO = ToGameObject(originField.Work[src - 1]);  // the row before
                Vector2 delta = GetPos(prevGO.transform) - GetPos(prevPrevGO.transform);
                SetPos(clone.transform, GetPos(root.transform) + delta);
            }

            // resolve every field's new element from the clone hierarchy
            foreach (var f in fields)
            {
                var srcGO = ToGameObject(f.Work[src]);
                if (srcGO == null) { f.Work.Add(null); continue; }
                if (!transformMap.TryGetValue(srcGO.transform, out var mappedT))
                {
                    Log.Warn($"UiRowExpander[{context}]: '{f.Info.Name}'[{src}] not covered by any clone root; duplicating directly.");
                    var solo = UnityEngine.Object.Instantiate(srcGO, srcGO.transform.parent, false);
                    mappedT = solo.transform;
                }
                f.Work.Add(ResolveElement(f, mappedT.gameObject));
            }
        }

        private static void Squish(List<FieldRef> fields, int origCount, int target, float extraScale, string context)
        {
            // Only reposition "root fields" (whose elements aren't children of another field's
            // element) — descendants follow their parent automatically.
            foreach (var f in fields)
            {
                if (IsChildField(f, fields)) continue;

                Vector2 anchor = GetPos(ToGameObject(f.Work[0]).transform);
                Vector2 origDelta = GetPos(ToGameObject(f.Work[1]).transform) - anchor;
                Vector2 newDelta = origDelta * ((float)(origCount - 1) / (target - 1));

                for (int i = 0; i < f.Work.Count; i++)
                {
                    var t = ToGameObject(f.Work[i])?.transform;
                    if (t == null) continue;
                    SetPos(t, anchor + newDelta * i);
                    if (extraScale > 0f && Math.Abs(extraScale - 1f) > 0.001f)
                        t.localScale = t.localScale * extraScale;
                }
            }
            Log.Debug($"UiRowExpander[{context}]: squished into original span (extraScale={extraScale}).");
        }

        // ---- helpers ----------------------------------------------------------

        private static bool IsChildField(FieldRef f, List<FieldRef> fields)
        {
            var go = ToGameObject(f.Work[0]);
            if (go == null) return false;
            foreach (var other in fields)
            {
                if (other == f) continue;
                var otherGO = ToGameObject(other.Work[0]);
                if (otherGO != null && otherGO != go && IsAncestor(otherGO.transform, go.transform))
                    return true;
            }
            return false;
        }

        private static object ResolveElement(FieldRef f, GameObject mappedGO)
        {
            if (typeof(GameObject).IsAssignableFrom(f.ElementType)) return mappedGO;
            if (typeof(Component).IsAssignableFrom(f.ElementType)) return mappedGO.GetComponent(f.ElementType);
            if (typeof(Transform).IsAssignableFrom(f.ElementType)) return mappedGO.transform;
            return mappedGO;
        }

        private static void Commit(Component host, FieldRef f)
        {
            if (f.IsArray)
            {
                var arr = Array.CreateInstance(f.ElementType, f.Work.Count);
                for (int i = 0; i < f.Work.Count; i++) arr.SetValue(f.Work[i], i);
                f.Info.SetValue(host, arr);
            }
            else
            {
                // append only the newly added entries to the live list
                while (f.List.Count < f.Work.Count)
                    f.List.Add(f.Work[f.List.Count]);
            }
        }

        private static GameObject ToGameObject(object element)
        {
            if (element == null) return null;
            if (element is GameObject go) return go;
            if (element is Component c) return c.gameObject;
            return null;
        }

        private static bool IsAncestor(Transform ancestor, Transform node)
        {
            for (var t = node.parent; t != null; t = t.parent)
                if (t == ancestor) return true;
            return false;
        }

        private static void BuildTransformMap(Transform orig, Transform clone, Dictionary<Transform, Transform> map)
        {
            map[orig] = clone;
            int n = Math.Min(orig.childCount, clone.childCount);
            for (int i = 0; i < n; i++)
                BuildTransformMap(orig.GetChild(i), clone.GetChild(i), map);
        }

        private static Vector2 GetPos(Transform t)
        {
            if (t is RectTransform rt) return rt.anchoredPosition;
            var p = t.localPosition; return new Vector2(p.x, p.y);
        }

        private static void SetPos(Transform t, Vector2 pos)
        {
            if (t is RectTransform rt) { rt.anchoredPosition = pos; return; }
            var p = t.localPosition; t.localPosition = new Vector3(pos.x, pos.y, p.z);
        }

        private static FieldInfo AccessTools(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (fi != null) return fi;
            }
            return null;
        }
    }
}
