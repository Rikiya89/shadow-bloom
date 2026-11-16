using System.Collections.Generic;
using UnityEngine;

namespace ShadowBloom
{
    /// <summary>
    /// The table garden: one small, static, fully opened lotus per solved campaign stage and recent Daily Shadow
    /// (up to 7, see <see cref="GardenPlan"/>), resting on the real surface just outside the target shadow (<see cref="GardenLayout"/>). Shares the bloom material and
    /// shader; each entry is one small draw call, meshes are cached per (petals, layers). Fades out whenever a bloom
    /// or a completion screen owns the table.
    /// </summary>
    public sealed class GardenController : MonoBehaviour
    {
        [SerializeField, Range(0.05f, 2f)] float _fadeSeconds = 0.3f;
        [Tooltip("Petal ribbon width, metres.")]
        [SerializeField] float _ribbonWidth = 0.0011f;
        [Tooltip("Brightness of past flowers, relative to the bloom's glow.")]
        [SerializeField, Range(0f, 2f)] float _intensityScale = 0.55f;
        [SerializeField, Range(0f, 1f)] float _restAlpha = 0.7f;

        [Header("Past flowers")]
        [Tooltip("Past flowers rest as plain paper drawings; only today's flower keeps the vermilion heart.")]
        [SerializeField] Color _pastColor = GeometryPalette.Paper;

        [Header("Today's flower")]
        [Tooltip("Today's flower keeps the bloom's vermilion heart, plus extra height and width.")]
        [SerializeField, Range(1f, 3f)] float _todayWidthScale = 1.25f;
        [SerializeField, Range(0f, 2f)] float _todayIntensity = 0.8f;

        readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();
        readonly List<GardenFlower> _dayFlowers = new List<GardenFlower>();
        readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>();
        readonly BloomGeometry _geometry = new BloomGeometry();
        MaterialPropertyBlock _mpb;
        Material _material;
        // Palette and glow come from the bloom in Init; nothing renders before it (no material, entries stay inactive).
        Color _inner, _outer, _tip;
        float _glow;
        float _fade, _fadeTarget;
        int _count;
        int _todayIndex = -1;
        float _scale = 1f;

        /// <summary>Flower size relative to the smallest garden (1 = 3 cm of free table behind the shadow).</summary>
        public float FlowerScale => _scale;

        public int ActiveCount => _count;
        public bool Visible => _fadeTarget > 0f;
        public int TodayIndex => _todayIndex;

        public void Init(BloomEffectController bloom)
        {
            _material = bloom.SharedMaterial;
            _inner = bloom.InnerColor;
            _outer = bloom.OuterColor;
            _tip = bloom.TipColor;
            _glow = bloom.Glow;
            _mpb ??= new MaterialPropertyBlock();
        }

        /// <param name="today">Day number whose flower is highlighted (none matches when it is not in the garden).</param>
        public void SetEntries(IReadOnlyList<int> days, float puzzleRadius, int today, float band = GardenLayout.MinBand)
        {
            _dayFlowers.Clear();
            if (days != null)
                for (int i = 0; i < days.Count; i++) _dayFlowers.Add(GardenPlan.Daily(days[i], days[i] == today));
            SetEntries(_dayFlowers, puzzleRadius, band);
        }

        /// <summary>Oldest first; when there are more flowers than slots, the newest are shown.</summary>
        /// <param name="band">Free table behind the shadow (<see cref="PuzzlePlacement.GardenBand"/>); flowers grow with it.</param>
        public void SetEntries(IReadOnlyList<GardenFlower> flowers, float puzzleRadius, float band = GardenLayout.MinBand)
        {
            _scale = GardenLayout.Scale(band);
            _mpb ??= new MaterialPropertyBlock();
            _count = flowers == null ? 0 : Mathf.Min(flowers.Count, GardenLayout.Order.Length);
            _todayIndex = -1;
            while (_renderers.Count < _count) _renderers.Add(CreateEntry(_renderers.Count));
            for (int i = 0; i < _renderers.Count; i++)
            {
                var r = _renderers[i];
                bool on = i < _count && _material != null;
                r.gameObject.SetActive(on);
                if (!on) continue;
                var flower = flowers[flowers.Count - _count + i];
                if (flower.Highlight) _todayIndex = i;
                int slot = GardenLayout.Order[i];
                float stem = GardenLayout.StemHeight(slot, band, flower.Highlight);
                r.GetComponent<MeshFilter>().sharedMesh = MeshFor(flower, _scale, stem);
                // The head sits on its stem above the slot. A taller stem, a wider line and the vermilion heart
                // identify the highlighted flower; its footprint stays inside the free band.
                r.transform.localPosition = GardenLayout.SlotLocal(slot, puzzleRadius, band) + Vector3.up * stem;
                r.transform.localRotation = Quaternion.Euler(0f, flower.Yaw, 0f);
                r.transform.localScale = Vector3.one;
            }
            Apply();
        }

        public void SetVisible(bool visible, bool instant = false)
        {
            _fadeTarget = visible ? 1f : 0f;
            if (instant) _fade = _fadeTarget;
            Apply();
        }

        void Update()
        {
            if (Mathf.Approximately(_fade, _fadeTarget)) return;
            _fade = Mathf.MoveTowards(_fade, _fadeTarget, Time.deltaTime / _fadeSeconds);
            Apply();
        }

        void Apply()
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                var r = _renderers[i];
                r.enabled = i < _count && _fade > 0.001f;
                if (!r.enabled) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat(BloomEffectController.BloomId, 1f);
                _mpb.SetFloat(BloomEffectController.StaggerId, 0.35f);
                _mpb.SetFloat(BloomEffectController.TwistId, 0f);
                _mpb.SetFloat(BloomEffectController.OvershootId, 0f);
                bool isToday = i == _todayIndex;
                _mpb.SetFloat(BloomEffectController.WidthId, _ribbonWidth * _scale * (isToday ? _todayWidthScale : 1f));
                _mpb.SetFloat(BloomEffectController.IntensityId, _glow * (isToday ? _todayIntensity : _intensityScale));
                _mpb.SetFloat(BloomEffectController.AlphaId, _restAlpha * _fade);
                // Past blooms rest as plain paper. Today's flower keeps the vermilion heart. Every colour the
                // shader reads is set; anything left unset would fall back to the shared material's palette.
                _mpb.SetFloat(BloomEffectController.WarmthId, 1f);
                _mpb.SetColor(BloomEffectController.ColorAId, isToday ? _inner : _pastColor);
                _mpb.SetColor(BloomEffectController.ColorBId, isToday ? _outer : _pastColor);
                _mpb.SetColor(BloomEffectController.ColorCId, isToday ? _tip : _pastColor);
                r.SetPropertyBlock(_mpb);
            }
        }

        MeshRenderer CreateEntry(int index)
        {
            var go = new GameObject("GardenBloom" + index);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
            return r;
        }

        Mesh MeshFor(in GardenFlower flower, float scale, float stem)
        {
            // Scale is fixed per placement and stems come in a few tiers, so only a handful of meshes ever exist.
            string key = $"{(int)flower.Shape}_{flower.Petals}_{flower.Layers}_{scale:F2}_{stem:F3}";
            if (_meshes.TryGetValue(key, out var mesh)) return mesh;
            var layout = BloomLayout.Default(flower.Petals, flower.Layers, GardenLayout.MiniRadius * scale, finale: false);
            layout.HaloWidth = 0f;
            layout.Petal = flower.Shape;
            layout.Stem = stem;
            _geometry.Build(layout, 0f);
            mesh = new Mesh { name = "GardenBloom_" + key };
            BloomMesh.Fill(mesh, _geometry);
            _meshes[key] = mesh;
            return mesh;
        }

        void OnDestroy()
        {
            foreach (var m in _meshes.Values) if (m != null) Destroy(m);
            _meshes.Clear();
        }
    }
}
