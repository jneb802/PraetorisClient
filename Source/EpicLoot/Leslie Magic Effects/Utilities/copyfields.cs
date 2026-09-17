using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EpicLootLeslieAlphaTest.src.Utilities;


public static class Help
{
    public static void CopyFieldsFrom<T, V>(this T target, V source)
        where T : Humanoid
        where V : Humanoid
    {
        Dictionary<string, FieldInfo> targetFields = typeof(T)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .ToDictionary(f => f.Name);

        FieldInfo[] sourceFields = typeof(V).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (FieldInfo sourceField in sourceFields)
        {
            if (!targetFields.TryGetValue(sourceField.Name, out FieldInfo targetField))
                continue;
            if (!targetField.FieldType.IsAssignableFrom(sourceField.FieldType))
                continue;

            targetField.SetValue(target, sourceField.GetValue(source));
        }
    }

    public static void Add<T>(this List<T> list, params T[] values) => list.AddRange(values);
    public static void Remove<T>(this GameObject prefab) where T : Component
    {
        if (prefab.TryGetComponent(out T component)) Object.DestroyImmediate(component);
    }
}
    
