namespace AIBridge.Editor
{
    internal static class AIBridgeEditorText
    {
        public static AIBridgeEditorLanguage Language
        {
            get { return AIBridgeProjectSettings.Instance.EditorLanguage; }
        }

        public static readonly string[] LanguageLabels =
        {
            "English",
            "简体中文"
        };

        public static readonly AIBridgeEditorLanguage[] LanguageValues =
        {
            AIBridgeEditorLanguage.English,
            AIBridgeEditorLanguage.SimplifiedChinese
        };

        public static int GetLanguageIndex(AIBridgeEditorLanguage language)
        {
            for (var i = 0; i < LanguageValues.Length; i++)
            {
                if (LanguageValues[i] == language)
                {
                    return i;
                }
            }

            return 0;
        }

        public static string T(string english, string simplifiedChinese)
        {
            return Language == AIBridgeEditorLanguage.SimplifiedChinese ? simplifiedChinese : english;
        }

        public static string For(AIBridgeEditorLanguage language, string english, string simplifiedChinese)
        {
            return language == AIBridgeEditorLanguage.SimplifiedChinese ? simplifiedChinese : english;
        }

        public static string[] LogRetrievalTypeLabels
        {
            get
            {
                return Language == AIBridgeEditorLanguage.SimplifiedChinese
                    ? new[] { "全部", "Info 及以上", "Warning 及以上", "Error" }
                    : new[] { "All", "Info and above", "Warning and above", "Error only" };
            }
        }
    }
}
