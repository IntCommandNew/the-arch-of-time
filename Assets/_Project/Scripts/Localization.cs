using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>The app's two languages.</summary>
public enum Language
{
    English,
    Romanian,
}

/// <summary>A text in both languages (the quest content and the UI strings use it).</summary>
[Serializable]
public class LocalizedText
{
    public string en;
    public string ro;

    /// <summary>The text in the current language (English when the Romanian one is missing).</summary>
    public string Text => In(Localization.Current);

    /// <summary>The text in the given language (English when the Romanian one is missing).</summary>
    public string In(Language language) => language == Language.Romanian && !string.IsNullOrEmpty(ro) ? ro : en;

    public override string ToString() => Text;
}

/// <summary>
/// The current language (saved in PlayerPrefs) and the UI strings, looked up by key from UiText.json.
/// Our own small system for the demo (not Unity Localization): the quest content carries both languages
/// itself (QuestContent), and the UI strings live in one table. Missing keys show as "[key]".
/// </summary>
public static class Localization
{
    const string LanguageKey = "Language";

    static readonly Dictionary<string, LocalizedText> strings = new Dictionary<string, LocalizedText>();

    /// <summary>Raised when the language changes.</summary>
    public static event Action Changed;

    /// <summary>The language in use. Until one is chosen: Romanian on a Romanian phone, else English.</summary>
    public static Language Current
    {
        get
        {
            if (PlayerPrefs.HasKey(LanguageKey))
                return (Language)Mathf.Clamp(PlayerPrefs.GetInt(LanguageKey), 0, 1);
            return Application.systemLanguage == SystemLanguage.Romanian ? Language.Romanian : Language.English;
        }
    }

    /// <summary>True once the user has picked a language (the language screen is shown until then).</summary>
    public static bool HasChosen => PlayerPrefs.HasKey(LanguageKey);

    /// <summary>The BCP 47 tag of the current language, for the narrator's voice.</summary>
    public static string LanguageTag => Current == Language.Romanian ? "ro-RO" : "en-GB";

    public static void Set(Language language)
    {
        bool changed = !HasChosen || language != Current;
        PlayerPrefs.SetInt(LanguageKey, (int)language);
        PlayerPrefs.Save();
        if (changed) Changed?.Invoke();
    }

    /// <summary>Loads the UI strings table (UiText.json: { "strings": [ { "key", "en", "ro" } ] }).</summary>
    public static void Load(TextAsset table)
    {
        strings.Clear();
        if (table == null) return;
        var parsed = JsonUtility.FromJson<Table>(table.text);
        if (parsed?.strings == null) return;
        foreach (var entry in parsed.strings)
            if (!string.IsNullOrEmpty(entry.key))
                strings[entry.key] = new LocalizedText { en = entry.en, ro = entry.ro };
    }

    /// <summary>The UI string for this key, in the current language.</summary>
    public static string Ui(string key)
    {
        return strings.TryGetValue(key, out var text) ? text.Text : "[" + key + "]";
    }

    /// <summary>The UI string for this key with {0}, {1}... filled in.</summary>
    public static string Ui(string key, params object[] args)
    {
        try
        {
            return string.Format(Ui(key), args);
        }
        catch (FormatException)
        {
            return Ui(key);
        }
    }

    /// <summary>The UI string for this key in the given language, with {0}, {1}... filled in (the narration
    /// recorder uses it to list both languages).</summary>
    public static string UiIn(Language language, string key, params object[] args)
    {
        if (!strings.TryGetValue(key, out var text)) return "[" + key + "]";
        try
        {
            return args == null || args.Length == 0 ? text.In(language) : string.Format(text.In(language), args);
        }
        catch (FormatException)
        {
            return text.In(language);
        }
    }

    [Serializable]
    class Table
    {
        public Entry[] strings;
    }

    [Serializable]
    class Entry
    {
        public string key;
        public string en;
        public string ro;
    }
}
