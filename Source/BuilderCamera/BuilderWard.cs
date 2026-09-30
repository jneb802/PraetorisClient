using System.Collections.Generic;
using UnityEngine;

namespace PraetorisClient.BuilderCameraFeature
{
    public sealed class BuilderWard : MonoBehaviour, Hoverable
    {
        internal static readonly List<BuilderWard> Instances = new List<BuilderWard>();

        private void Awake()
        {
            ZNetView view = GetComponent<ZNetView>();
            if (view && view.IsValid()) Instances.Add(this);
        }

        private void OnDestroy() { Instances.Remove(this); }

        public string GetHoverName() => "Builder's Ward";
        public float GetHoverOffset() => 0f;
        public string GetHoverText() => $"Builder's Ward\nEquip Builder Belt + build tool; {BuilderCamera.ToggleKey.Value} toggles camera";
    }
}
