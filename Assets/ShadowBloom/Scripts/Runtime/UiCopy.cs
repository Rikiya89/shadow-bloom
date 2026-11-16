using System;
using UnityEngine;

namespace ShadowBloom
{
    public enum UiLanguage
    {
        English,
        Japanese
    }

    /// <summary>
    /// Which language the panel speaks. On a headset it follows the device language (Japanese, otherwise English),
    /// so an English device always gets the English game. In the Editor it stays English, so tests and previews
    /// are the same on every machine; set the environment variable SB_LANG=ja to preview Japanese there.
    /// </summary>
    public static class UiLocale
    {
        /// <summary>Forces a language (tests, previews). Null follows the device.</summary>
        public static UiLanguage? Override;
        static UiLanguage? _detected;

        public static UiLanguage Current => Override ?? (_detected ??= Detect());

        static UiLanguage Detect()
        {
            string env = Environment.GetEnvironmentVariable("SB_LANG");
            if (env == "ja") return UiLanguage.Japanese;
            if (env == "en") return UiLanguage.English;
#if UNITY_EDITOR
            return UiLanguage.English;
#else
            return Application.systemLanguage == SystemLanguage.Japanese ? UiLanguage.Japanese : UiLanguage.English;
#endif
        }
    }

    /// <summary>
    /// Every player-facing UI string, in one place, in English and Japanese (<see cref="UiLocale"/>). English is
    /// short, plain and ASCII only (the Latin font atlas has no arrows, ticks or ellipses, and a missing glyph renders
    /// as a box); Japanese uses only characters baked into the Noto Serif JP atlas, which is built from these
    /// strings (Shadow Bloom > Build Japanese UI Font). Also the two small decisions that turn puzzle state into
    /// words: the stage 1 coaching step and the qualitative alignment label. The alignment value is a squared,
    /// per-stage-normalised closeness curve, not a calibrated accuracy, so it is never shown as a percentage.
    /// </summary>
    public static class UiCopy
    {
        static bool Ja => UiLocale.Current == UiLanguage.Japanese;
        static string T(string en, string ja) => Ja ? ja : en;

        public const string GameTitle = "Shadow Bloom";
        public static string Tagline => T("Align the shadow. Let it bloom.", "影を合わせて、花を咲かせよう。");

        // Setup
        public static string SetupSearching => T("SEARCHING", "さがしています");
        public static string SetupFound => T("SURFACE FOUND", "テーブルを見つけました");
        public static string SetupManual => T("SET UP YOUR TABLE", "テーブルを設定");
        static readonly string[] SetupStepLinesEn = { "1. Pinch the ring.", "2. Set it down on your table.", "3. Press Confirm." };
        static readonly string[] SetupStepLinesJa = { "1. リングをつまむ。", "2. テーブルの上に置く。", "3. 「決定」を押す。" };
        static string[] SetupStepLines => Ja ? SetupStepLinesJa : SetupStepLinesEn;
        public static string SetupSteps => string.Join("\n", SetupStepLines);
        public static string SetupBegin => T("Press Start. The puzzle appears on your table.", "スタートを押すと、テーブルにパズルが現れます。");
        public static string Start => T("Start", "スタート");
        public static string Confirm => T("Confirm", "決定");

        // Play
        public static string Retry => T("Retry", "やり直す");
        public static string Pause => T("Pause", "ポーズ");
        public static string Move => T("Move", "移動");
        public static string Aligned => T("Aligned.", "そろいました。");
        public static string NextStage => T("Next stage", "次のステージ");
        public static string Finish => T("Finish", "完了");

        // Pause
        public static string Paused => T("PAUSED", "ポーズ中");
        public static string Resume => T("Resume", "再開");
        public static string RestartAll => T("Restart from stage 1", "ステージ1からやり直す");
        public static string SoundOn => T("Sound: On", "サウンド: オン");
        public static string SoundOff => T("Sound: Off", "サウンド: オフ");
        public static string MotionFull => T("Motion: Full", "動き: ふつう");
        public static string MotionCalm => T("Motion: Calm", "動き: ひかえめ");

        // Completion
        public static string Complete => T("Bloom complete", "すべて咲きました");
        public static string PlayAgain => T("Play again", "もう一度あそぶ");

        // Recovery
        public static string HandsLost => T("Hands not visible. Hold them up in front of you.", "手が見えません。体の前に上げてください。");

        public static string AssistOn => T("Assist: On", "アシスト: オン");
        public static string AssistOff => T("Assist: Off", "アシスト: オフ");

        // Daily Shadow
        public static string DailyShadow => T("Daily Shadow", "今日の影");
        public static string DailyReplay => T("Replay today's shadow", "今日の影をもう一度");
        public static string DailyDoneTitle => T("Today's bloom", "今日の花");
        public static string DailyGardenHint => T("Today's flower is in your garden. More tomorrow.", "今日の花が庭に咲きました。また明日。");
        public static string DailyReady => T("Solve Daily Shadow to add a garden flower.", "「今日の影」を解くと、庭に花が増えます。");
        public static string BackToStages => T("Stages", "ステージへ");
        static readonly string[] WeekdaysJa = { "日曜日", "月曜日", "火曜日", "水曜日", "木曜日", "金曜日", "土曜日" };
        public static string DailyEyebrow(DayOfWeek day) =>
            Ja ? $"今日の影 - {WeekdaysJa[(int)day]}" : $"DAILY SHADOW - {day.ToString().ToUpperInvariant()}";
        public static string DailyDoneSubtitle(int streak, int garden, int capacity) =>
            Ja ? $"連続 {streak}日 - 今日の花 {garden}/{capacity}" : $"Streak {streak} - Daily flowers {garden}/{capacity}";

        public static string StageEyebrow(int index, int count) =>
            Ja ? $"ステージ {index + 1} / {count}" : $"STAGE {index + 1} OF {count}";
        public static string CompleteEyebrow(int count) =>
            Ja ? $"{count}つの影がそろいました" : $"ALL {count} SHADOWS ALIGNED";

        // ---- Placement status ---------------------------------------------------------------------------
        // The placement code reports short English reasons (they are also its log lines); the panel shows them
        // through this lookup. An unknown reason is shown as it is.

        static readonly (string en, string ja)[] PlacementMessages =
        {
            ("Looking for a table...", "テーブルをさがしています"),
            ("Table found.", "テーブルを見つけました。"),
            ("Surface found.", "置ける面を見つけました。"),
            ("No table found.", "テーブルが見つかりません。"),
            ("No table within reach.", "手の届くテーブルがありません。"),
            ("No room scan found.", "部屋のスキャンがありません。"),
            ("Room access was denied.", "部屋の情報が許可されていません。"),
            ("Room data unavailable.", "部屋の情報を使えません。"),
            ("Placed.", "配置しました。")
        };

        public static string PlacementMessage(string message)
        {
            if (!Ja || string.IsNullOrEmpty(message)) return message;
            foreach (var (en, ja) in PlacementMessages)
                if (en == message) return ja;
            return message;
        }

        // ---- Stage names and hints ----------------------------------------------------------------------
        // English lives on the stage assets (designers tune it there); Japanese is looked up by curve family, which
        // also covers the generated Daily Shadow stages.

        static (string name, string hint) StageJa(CurveKind kind)
        {
            switch (kind)
            {
                case CurveKind.Ring: return ("円", "リングを傾けて、影を円にしましょう。");
                case CurveKind.Star: return ("星", "星を寝かせて、すべての角を合わせましょう。");
                case CurveKind.Rose: return ("花びら", "花を寝かせて回し、花びらをそろえましょう。");
                case CurveKind.Viviani: return ("無限", "今度は円ではありません。8の字を見つけましょう。");
                case CurveKind.TorusKnot: return ("結び目", "結び目の影が三つ葉の星になる角度を探しましょう。");
                case CurveKind.Orbit: return ("軌道", "3つの軌道を1つの影に。輪をすべて合わせましょう。");
                case CurveKind.Seed: return ("種", "7つの円で1つの花。ぴったり重ねましょう。");
                case CurveKind.Lissajous: return ("織り", "1本の糸に、たくさんの影。正しい影を見つけましょう。");
                default: return ("開花", "すべての層をそろえて、咲かせましょう。");
            }
        }

        public static string StageName(PuzzleStage stage) => Ja ? StageJa(stage.curve.kind).name : stage.displayName;
        public static string StageHint(PuzzleStage stage) => Ja ? StageJa(stage.curve.kind).hint : stage.hint;

        // ---- How to play: cards before stage 1 (first launch) and from the pause panel ------------------

        public static string HowToPlay => T("How to play", "あそびかた");
        public static string HowToPlayEyebrow => T("HOW TO PLAY", "あそびかた");
        public static string HowToPlayNext => T("Next", "次へ");
        public static string HowToPlayDone => T("Start playing", "はじめる");

        // One idea per card, in the words the stage 1 coaching and the alignment label use. Each body is two short
        // sentences (each within the short-string limit), shown as one paragraph.
        static readonly string[] CardTitlesEn = { "The goal", "Grab and turn", "Match and hold" };
        static readonly string[] CardTitlesJa = { "目的", "つまんで回す", "合わせて止める" };
        static readonly string[][] CardBodiesEn =
        {
            // TODO(you): the first words a new player reads
            new[] { "The glowing shape casts a solid shadow.", "Make it fit the dashed outline on your table." },
            new[] { "Pinch the shape to grab it.", "Turn your hand and watch the shadow change." },
            new[] { "When the shadow fits, you see HOLD STEADY.", "Keep still for a moment and it blooms." }
        };
        static readonly string[][] CardBodiesJa =
        {
            // TODO(you): 新しいプレイヤーが最初に読む言葉。自然な日本語に直してください。
            new[] { "光る形は、テーブルに実線の影を落とします。", "その影を点線の形にぴったり合わせましょう。" },
            new[] { "形をつまんで持ちます。", "手を回すと、影の形が変わります。" },
            new[] { "影が合うと「そのまま」と表示されます。", "少し止めていると、花が咲きます。" }
        };
        static string[] CardTitles => Ja ? CardTitlesJa : CardTitlesEn;
        static string[][] CardBodies => Ja ? CardBodiesJa : CardBodiesEn;

        public static int HowToPlayCount => CardTitlesEn.Length;
        /// <summary>Cards shown on first launch. The stage 1 coaching and ghost demo teach the rest in context;
        /// Pause > How to play shows all <see cref="HowToPlayCount"/>.</summary>
        public const int FirstRunCardCount = 1;
        public static string HowToPlayTitle(int card) => CardTitles[Math.Clamp(card, 0, CardTitles.Length - 1)];
        // Japanese has no spaces between sentences.
        public static string HowToPlayBody(int card) => string.Join(Ja ? "" : " ", CardBodies[Math.Clamp(card, 0, CardBodies.Length - 1)]);

        // ---- Stage 1 coaching: one short instruction at a time ---------------------------------------

        public const int StepGrab = 0, StepTurn = 1, StepMatch = 2, StepHold = 3;

        static readonly string[] StepsEn =
        {
            "Pinch the floating ring to grab it.",
            "Turn the ring to move its solid shadow.",
            "Move the solid shadow onto the dashed circle.",
            "Hold still to bloom."
        };
        static readonly string[] StepsJa =
        {
            "浮かんでいるリングをつまんで持ちます。",
            "リングを回して、実線の影を動かします。",
            "実線の影を点線の円に重ねます。",
            "そのまま止めると花が咲きます。"
        };
        static string[] Steps => Ja ? StepsJa : StepsEn;

        public static string TutorialText(int step) => Steps[Math.Clamp(step, 0, Steps.Length - 1)];

        /// <summary>
        /// Advances the coaching step from what the player has demonstrated. It only moves forward through grab and
        /// turn (once shown, never repeated), and moves between "match" and "hold" as the shape enters or leaves
        /// tolerance, so the text never flickers back to an instruction the player already followed.
        /// </summary>
        public static int NextTutorialStep(int step, bool hasGrabbed, float alignment01, bool within)
        {
            if (step == StepGrab && hasGrabbed) step = StepTurn;
            if (step == StepTurn && alignment01 >= 0.35f) step = StepMatch;
            if (step >= StepMatch) step = within ? StepHold : StepMatch;
            return step;
        }

        // ---- Alignment ---------------------------------------------------------------------------------

        public enum AlignmentLevel { Far, Closer, Almost, Hold }

        public static AlignmentLevel Level(float alignment01, bool within)
        {
            if (within) return AlignmentLevel.Hold;
            if (alignment01 >= 0.6f) return AlignmentLevel.Almost;
            if (alignment01 >= 0.15f) return AlignmentLevel.Closer;
            return AlignmentLevel.Far;
        }

        public static string AlignmentLabel(AlignmentLevel level)
        {
            switch (level)
            {
                case AlignmentLevel.Hold: return T("HOLD STEADY", "そのまま");
                case AlignmentLevel.Almost: return T("ALMOST", "あと少し");
                case AlignmentLevel.Closer: return T("CLOSER", "近づいています");
                default: return T("ROTATE TO ALIGN", "回して合わせよう");
            }
        }

        /// <summary>All fixed strings in the current language, for the glyph-coverage test and the Japanese font atlas.</summary>
        public static System.Collections.Generic.IEnumerable<string> AllFixedStrings()
        {
            yield return GameTitle; yield return Tagline;
            yield return SetupSearching; yield return SetupFound; yield return SetupManual; yield return SetupBegin;
            foreach (var s in SetupStepLines) yield return s;
            yield return HowToPlay; yield return HowToPlayEyebrow; yield return HowToPlayNext; yield return HowToPlayDone;
            foreach (var s in CardTitles) yield return s;
            foreach (var body in CardBodies) foreach (var s in body) yield return s;
            yield return Start; yield return Confirm; yield return Retry; yield return Pause; yield return Move;
            yield return Aligned; yield return NextStage; yield return Finish; yield return Paused; yield return Resume;
            yield return RestartAll; yield return SoundOn; yield return SoundOff; yield return MotionFull;
            yield return MotionCalm; yield return Complete; yield return PlayAgain; yield return HandsLost;
            yield return StageEyebrow(4, 5); yield return CompleteEyebrow(5);
            yield return AssistOn; yield return AssistOff; yield return DailyShadow; yield return DailyReplay;
            yield return DailyDoneTitle; yield return DailyReady; yield return BackToStages;
            yield return DailyGardenHint;
            foreach (DayOfWeek d in Enum.GetValues(typeof(DayOfWeek))) yield return DailyEyebrow(d);
            foreach (var (en, _) in PlacementMessages) yield return PlacementMessage(en);
            foreach (CurveKind kind in Enum.GetValues(typeof(CurveKind)))
            {
                var ja = StageJa(kind);
                if (Ja) { yield return ja.name; yield return ja.hint; }
            }
            yield return DailyDoneSubtitle(12, 7, 7);
            foreach (var s in Steps) yield return s;
            foreach (AlignmentLevel l in Enum.GetValues(typeof(AlignmentLevel))) yield return AlignmentLabel(l);
        }
    }
}
