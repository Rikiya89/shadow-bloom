using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace ShadowBloom.EditorTools
{
    /// <summary>
    /// Builds the panel's book typeface: static TMP SDF font assets for Source Serif 4 (SIL OFL 1.1, Adobe; files and
    /// licence in Assets/ShadowBloom/Fonts) with printable ASCII only, because all UI copy is ASCII
    /// (<c>UiCopyTests</c>). Semibold is the Regular asset's bold face, so FontStyles.Bold uses real Semibold glyphs
    /// instead of TMP's synthetic bold. LiberationSans stays as a fallback. Assigns the result to UiTheme.font.
    /// Idempotent: rebuilds the two assets in place, keeping their GUIDs.
    /// </summary>
    public static class ShadowBloomFonts
    {
        const string Dir = ShadowBloomSceneBuilder.Root + "/Fonts";
        const string RegularTtf = Dir + "/SourceSerif4-Regular.ttf";
        const string SemiboldTtf = Dir + "/SourceSerif4-Semibold.ttf";
        public const string RegularAsset = Dir + "/SourceSerif4-Regular SDF.asset";
        const string SemiboldAsset = Dir + "/SourceSerif4-Semibold SDF.asset";
        const string FallbackAsset = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        // Sampling 72 px with 9 px padding (12.5 %) keeps the SDF edge clean at the panel's 6-32 mm sizes.
        const int SamplingSize = 72, Padding = 9, AtlasSize = 1024;

        [MenuItem("Shadow Bloom/Build UI Fonts", priority = 3)]
        static void Menu() => Debug.Log("[ShadowBloom] " + Build());

        public static string Build()
        {
            var ascii = Ascii();
            var semibold = BuildAsset(SemiboldTtf, SemiboldAsset, ascii);
            var regular = BuildAsset(RegularTtf, RegularAsset, ascii);

            // The getter returns the serialized array itself (its setter is internal), so set the slot in place.
            var weights = regular.fontWeightTable;
            if (weights == null || weights.Length < 8) throw new System.InvalidOperationException("TMP font weight table missing.");
            weights[7].regularTypeface = semibold;   // 700 = FontStyles.Bold
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackAsset);
            if (fallback != null)
            {
                regular.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fallback };
                semibold.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fallback };
            }
            EditorUtility.SetDirty(regular);
            EditorUtility.SetDirty(semibold);

            var theme = AssetDatabase.LoadAssetAtPath<UiTheme>(ShadowBloomUiUpgrade.ThemePath);
            if (theme == null) throw new System.InvalidOperationException("UiTheme.asset missing.");
            theme.font = regular;
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            return $"UI fonts built: {RegularAsset} (+ Semibold as bold), {regular.characterTable.Count} characters; theme font set.";
        }

        const string JaRegularOtf = Dir + "/NotoSerifJP-Regular.otf";
        const string JaSemiboldOtf = Dir + "/NotoSerifJP-SemiBold.otf";
        public const string JaRegularAsset = Dir + "/NotoSerifJP-Regular SDF.asset";
        const string JaSemiboldAsset = Dir + "/NotoSerifJP-SemiBold SDF.asset";
        const int JaSamplingSize = 60, JaPadding = 8, JaAtlasSize = 2048;

        [MenuItem("Shadow Bloom/Build Japanese UI Font", priority = 4)]
        static void MenuJapanese() => Debug.Log("[ShadowBloom] " + BuildJapanese());

        /// <summary>
        /// Builds static Noto Serif JP atlases (SIL OFL 1.1, licence beside the fonts) holding exactly the characters the
        /// Japanese UI copy uses, plus all kana and common punctuation, and adds them as the first fallback of the
        /// Source Serif assets: Latin text keeps its typeface, Japanese falls through to Noto. Only the atlases ship;
        /// the 6 MB font files stay in the Editor. Run again after changing Japanese text that adds a new kanji
        /// (UiCopyTests fails until you do). Idempotent.
        /// </summary>
        public static string BuildJapanese()
        {
            string characters = JapaneseCharacters();
            var jaSemibold = BuildAsset(JaSemiboldOtf, JaSemiboldAsset, characters, JaSamplingSize, JaPadding, JaAtlasSize);
            var jaRegular = BuildAsset(JaRegularOtf, JaRegularAsset, characters, JaSamplingSize, JaPadding, JaAtlasSize);

            var regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(RegularAsset);
            var semibold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SemiboldAsset);
            if (regular == null || semibold == null) throw new System.InvalidOperationException("Build the Latin UI fonts first.");
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackAsset);
            regular.fallbackFontAssetTable = Fallbacks(jaRegular, fallback);
            semibold.fallbackFontAssetTable = Fallbacks(jaSemibold, fallback);
            EditorUtility.SetDirty(regular);
            EditorUtility.SetDirty(semibold);
            AssetDatabase.SaveAssets();
            return $"Japanese UI font built: {JaRegularAsset} (+ SemiBold), {jaRegular.characterTable.Count} characters; set as fallback of the Source Serif assets.";
        }

        static System.Collections.Generic.List<TMP_FontAsset> Fallbacks(TMP_FontAsset japanese, TMP_FontAsset last)
        {
            var list = new System.Collections.Generic.List<TMP_FontAsset> { japanese };
            if (last != null) list.Add(last);
            return list;
        }

        /// <summary>Every non-ASCII character of the Japanese copy, plus all hiragana, katakana and common punctuation.</summary>
        public static string JapaneseCharacters()
        {
            var set = new System.Collections.Generic.SortedSet<char>();
            for (char c = '\u3041'; c <= '\u3096'; c++) set.Add(c);   // hiragana
            for (char c = '\u30A1'; c <= '\u30FC'; c++) set.Add(c);   // katakana, middle dot, long vowel mark
            foreach (char c in "\u3001\u3002\u300C\u300D\uFF01\uFF1F\u3000") set.Add(c);   // 、。「」！？ and the wide space
            var before = UiLocale.Override;
            try
            {
                UiLocale.Override = UiLanguage.Japanese;
                foreach (var s in UiCopy.AllFixedStrings())
                    foreach (char c in s)
                        if (c >= 128) set.Add(c);
            }
            finally
            {
                UiLocale.Override = before;
            }
            return new string(new System.Collections.Generic.List<char>(set).ToArray());
        }

        static string Ascii()
        {
            var sb = new System.Text.StringBuilder();
            for (char c = (char)32; c < 127; c++) sb.Append(c);
            return sb.ToString();
        }

        static TMP_FontAsset BuildAsset(string ttfPath, string assetPath, string characters,
            int samplingSize = SamplingSize, int padding = Padding, int atlasSize = AtlasSize)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (font == null) throw new System.InvalidOperationException($"{ttfPath} missing.");

            var built = TMP_FontAsset.CreateFontAsset(font, samplingSize, padding, GlyphRenderMode.SDFAA, atlasSize, atlasSize,
                AtlasPopulationMode.Dynamic, false);
            if (!built.TryAddCharacters(characters, out string missing))
                throw new System.InvalidOperationException($"{ttfPath}: glyphs missing for '{missing}'.");
            built.atlasPopulationMode = AtlasPopulationMode.Static;
            built.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);

            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            TMP_FontAsset target;
            if (existing == null)
            {
                AssetDatabase.CreateAsset(built, assetPath);
                target = built;
            }
            else
            {
                // Keep the asset (and its GUID) so prefab references survive a rebuild; replace its contents.
                foreach (var sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
                    if (sub != null) AssetDatabase.RemoveObjectFromAsset(sub);
                EditorUtility.CopySerialized(built, existing);
                existing.name = built.name;
                target = existing;
            }

            var atlas = built.atlasTexture;
            atlas.name = built.name + " Atlas";
            var material = built.material;
            material.name = built.name + " Material";
            AssetDatabase.AddObjectToAsset(atlas, target);
            AssetDatabase.AddObjectToAsset(material, target);
            if (target != built)
            {
                target.atlasTextures = new[] { atlas };
                target.material = material;
                Object.DestroyImmediate(built, true);
            }
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath);
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        }
    }
}
