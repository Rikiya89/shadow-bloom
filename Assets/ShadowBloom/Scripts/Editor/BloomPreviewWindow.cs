using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShadowBloom.EditorTools
{
    /// <summary>
    /// Hidden, never-saved copy of the scene's bloom for Edit-mode inspection. Copies the scene controller's tuned
    /// parameters so the preview matches what the game plays.
    /// </summary>
    internal sealed class BloomPreviewRig : IDisposable
    {
        public const string MaterialPath = ShadowBloomSceneBuilder.Root + "/Materials/M_Bloom.mat";

        public readonly GameObject Root;
        public readonly BloomEffectController Bloom;
        /// <summary>The scene's puzzle (may be null): its target colours seed the preview like the real solve.</summary>
        public readonly PuzzleController Puzzle;

        public BloomPreviewRig(BloomEffectController template)
        {
            Root = EditorUtility.CreateGameObjectWithHideFlags("BloomPreview (not saved)", HideFlags.HideAndDontSave,
                typeof(MeshFilter), typeof(MeshRenderer));
            var filter = Root.GetComponent<MeshFilter>();
            var renderer = Root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = template != null
                ? template.GetComponent<MeshRenderer>().sharedMaterial
                : AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Bloom = Root.AddComponent<BloomEffectController>();
            if (template != null)
            {
                EditorUtility.CopySerialized(template, Bloom);
                Root.transform.SetPositionAndRotation(template.transform.position, template.transform.rotation);
            }
            var so = new SerializedObject(Bloom);
            so.FindProperty("_filter").objectReferenceValue = filter;
            so.FindProperty("_renderer").objectReferenceValue = renderer;
            so.ApplyModifiedPropertiesWithoutUndo();
            Puzzle = UnityEngine.Object.FindAnyObjectByType<PuzzleController>();
        }

        /// <summary>The colour the real solve would start <paramref name="stage"/>'s bloom in (null = the
        /// controller's fallback when the scene has no puzzle).</summary>
        public Color? SeedColor(PuzzleStage stage) => Puzzle != null ? Puzzle.SolvedTargetDisplayColor(stage) : (Color?)null;

        public void Dispose()
        {
            if (Root == null) return;
            var mesh = Root.GetComponent<MeshFilter>().sharedMesh;
            if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(Root);
        }
    }

    /// <summary>
    /// Shadow Bloom > Bloom Preview. Edit mode: scrub or loop any stage's bloom without solving a puzzle or a headset.
    /// Play mode: trigger the real controller (with its audio and plate events) or the real solve path.
    /// Editor assembly only, so none of this ships.
    /// </summary>
    public sealed class BloomPreviewWindow : EditorWindow
    {
        const float RestHold = 1.5f;

        PuzzleStage[] _stages;
        int _stageIndex;
        bool _finale;
        float _radius = 0.12f;
        bool _reducedMotion;
        float _time;
        bool _looping;
        float _speed = 1f;
        double _lastTick;
        BloomPreviewRig _rig;

        [MenuItem("Shadow Bloom/Bloom Preview", priority = 20)]
        static void Open() => GetWindow<BloomPreviewWindow>("Bloom Preview");

        void OnEnable()
        {
            LoadStages();
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += DisposeRig;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= DisposeRig;
            DisposeRig();
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) DisposeRig();
            _looping = false;
            LoadStages();
            Repaint();
        }

        void LoadStages()
        {
            var game = FindAnyObjectByType<GameStateController>();
            _stages = game != null && game.Stages != null && game.Stages.Length > 0 && game.Stages.All(s => s != null)
                ? game.Stages
                : DefaultStages.CreateAll();
            _stageIndex = Mathf.Clamp(_stageIndex, 0, _stages.Length - 1);
        }

        PuzzleStage Stage => _stages[_stageIndex];

        static bool IsFinaleStage(PuzzleStage[] stages, int i) =>
            i == stages.Length - 1 || stages[i].completion == CompletionBehavior.FinalBloom;

        void OnGUI()
        {
            if (_stages == null || _stages.Length == 0) LoadStages();

            EditorGUI.BeginChangeCheck();
            int stage = EditorGUILayout.Popup("Stage", _stageIndex,
                _stages.Select((s, i) => $"{i + 1:00} {s.displayName}  ({s.bloomPetals} petals x {s.bloomLayers})").ToArray());
            if (stage != _stageIndex)
            {
                _stageIndex = stage;
                _finale = IsFinaleStage(_stages, stage);
            }
            _finale = EditorGUILayout.Toggle("Finale composition", _finale);
            _radius = EditorGUILayout.Slider("Puzzle radius (m)", _radius, SurfacePlacementSolver.MinRadius, SurfacePlacementSolver.MaxRadius);
            _reducedMotion = EditorGUILayout.Toggle("Reduced motion", _reducedMotion);
            bool changed = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space();
            if (EditorApplication.isPlaying) PlayModeGUI();
            else EditModeGUI(changed);
        }

        void EditModeGUI(bool settingsChanged)
        {
            EnsureRig();
            float total = _rig.Bloom.TotalDuration + RestHold;

            EditorGUI.BeginChangeCheck();
            _time = EditorGUILayout.Slider("Time (s)", _time, 0f, total);
            if (EditorGUI.EndChangeCheck()) _looping = false;
            EditorGUILayout.LabelField("Phase", PhaseLabel(_rig.Bloom, _time));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_looping ? "Pause" : "Play loop")) { _looping = !_looping; _lastTick = EditorApplication.timeSinceStartup; }
                if (GUILayout.Button("Restart")) _time = 0f;
                if (GUILayout.Button("Frame in Scene view")) FrameSceneView();
            }
            _speed = EditorGUILayout.Slider("Speed", _speed, 0.1f, 2f);
            EditorGUILayout.HelpBox("Preview object is hidden and never saved; tuning comes from the scene's BloomEffectController. " +
                                    "Edit that component (or M_Bloom.mat) and the preview follows.", MessageType.None);
            ApplyPreview();
            if (settingsChanged) SceneView.RepaintAll();
        }

        void PlayModeGUI()
        {
            var bloom = FindAnyObjectByType<BloomEffectController>();
            var game = FindAnyObjectByType<GameStateController>();
            var puzzle = FindAnyObjectByType<PuzzleController>();
            if (bloom == null)
            {
                EditorGUILayout.HelpBox("No BloomEffectController in the playing scene.", MessageType.Warning);
                return;
            }
            bloom.ReducedMotion = _reducedMotion;
            if (GUILayout.Button("Play bloom (cosmetic, no game state change)"))
                bloom.Play(Stage.bloomPetals, Stage.bloomLayers, puzzle != null ? puzzle.PuzzleRadius : _radius, _finale, Stage,
                    seedColor: puzzle != null ? puzzle.SolvedTargetDisplayColor(Stage) : (Color?)null);
            if (GUILayout.Button("Stop bloom")) bloom.Stop();
            using (new EditorGUI.DisabledScope(game == null || puzzle == null || !game.Flow.IsInteractive))
            {
                if (GUILayout.Button("Solve current stage (real solve path)")) puzzle.DebugSetSolution();
            }
            EditorGUILayout.LabelField("Bloom", bloom.IsPlaying ? (bloom.IsFinished ? "resting" : "unfolding") : "idle");
            if (game != null) EditorGUILayout.LabelField("Game", $"{game.Flow.State}, stage {game.Flow.StageIndex + 1}/{game.Flow.StageCount}");
        }

        static string PhaseLabel(BloomEffectController b, float t)
        {
            float lead = b.LockLead;
            if (t < lead) return "Lock (snap + swell + chime)";
            if (t < lead + b.MainDuration) return $"Bloom {Mathf.Clamp01((t - lead) / b.MainDuration):P0}";
            if (t < b.TotalDuration) return "Resolve (success swell)";
            return "Rest (breath)";
        }

        void EnsureRig()
        {
            if (_rig != null && _rig.Root != null) return;
            _rig = new BloomPreviewRig(FindAnyObjectByType<BloomEffectController>());
        }

        void ApplyPreview()
        {
            if (_rig == null || _rig.Root == null) return;
            _rig.Bloom.ReducedMotion = _reducedMotion;
            _rig.Bloom.PreviewAt(Stage.bloomPetals, Stage.bloomLayers, _radius, _finale, _time, Stage, _rig.SeedColor(Stage));
        }

        void Tick()
        {
            if (!_looping || EditorApplication.isPlayingOrWillChangePlaymode || _rig == null) return;
            double now = EditorApplication.timeSinceStartup;
            _time += (float)(now - _lastTick) * _speed;
            _lastTick = now;
            if (_time > _rig.Bloom.TotalDuration + RestHold) _time = 0f;
            ApplyPreview();
            SceneView.RepaintAll();
            Repaint();
        }

        void FrameSceneView()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null || _rig == null) return;
            view.Frame(new Bounds(_rig.Root.transform.position, Vector3.one * _radius * 4.5f), false);
        }

        void DisposeRig()
        {
            _rig?.Dispose();
            _rig = null;
        }
    }
}
