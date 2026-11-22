using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Who speaks a line: Mihail Sturdza (greeting, seals, his letter) or the narrator (the rest).</summary>
public enum VoiceRole
{
    Narrator,
    Sturdza,
}

/// <summary>One spoken line: who says it and the exact text.</summary>
public readonly struct NarrationLine
{
    public readonly VoiceRole Role;
    public readonly string Text;

    public NarrationLine(VoiceRole role, string text)
    {
        Role = role;
        Text = text;
    }
}

/// <summary>
/// The recorded narration: one audio clip per spoken line, voice and language, made with ElevenLabs by the
/// Editor tool NarrationRecorder (Tools > The Arch of Time > Record narration). A recording is found by its
/// exact text, so a line whose text changed in the content has no recording until it is recorded again
/// (the Narrator then falls back to Android's voice for it and logs a warning).
/// </summary>
[CreateAssetMenu(menuName = "The Arch of Time/Narration library")]
public class NarrationLibrary : ScriptableObject
{
    [Serializable]
    public class Recording
    {
        public VoiceRole role;
        public Language language;
        [TextArea] public string text;
        public AudioClip clip;
    }

    [Header("ElevenLabs (used only by the Editor recorder)")]
    [Tooltip("The text-to-speech model.")]
    public string modelId = "eleven_v4";

    [Tooltip("The narrator's voice ID (a voice designed in the user's ElevenLabs account).")]
    public string narratorVoiceId = "qUwKKsEYPewoILlxCIx6";

    [Tooltip("Mihail Sturdza's voice ID.")]
    public string sturdzaVoiceId = "wsy3f26m7s6wq8zKCtJK";

    [Header("Recordings (filled by the recorder)")]
    public List<Recording> recordings = new List<Recording>();

    Dictionary<string, AudioClip> lookup;
    readonly HashSet<Language> languages = new HashSet<Language>();

    /// <summary>The key of a line: who, which language, the text.</summary>
    public static string Key(VoiceRole role, Language language, string text) =>
        role + "|" + language + "|" + (text ?? "").Trim();

    public string VoiceId(VoiceRole role) => role == VoiceRole.Sturdza ? sturdzaVoiceId : narratorVoiceId;

    /// <summary>The recording of this line, or null.</summary>
    public AudioClip Find(VoiceRole role, Language language, string text)
    {
        BuildLookup();
        return lookup.TryGetValue(Key(role, language, text), out var clip) ? clip : null;
    }

    /// <summary>True when there are recordings in this language (its narration doesn't need Android's voice).</summary>
    public bool Covers(Language language)
    {
        BuildLookup();
        return languages.Contains(language);
    }

    /// <summary>Forget the lookup (the recorder changed the recordings).</summary>
    public void Invalidate() => lookup = null;

    void OnEnable() => lookup = null;

    void BuildLookup()
    {
        if (lookup != null) return;
        lookup = new Dictionary<string, AudioClip>();
        languages.Clear();
        foreach (var recording in recordings)
        {
            if (recording?.clip == null) continue;
            lookup[Key(recording.role, recording.language, recording.text)] = recording.clip;
            languages.Add(recording.language);
        }
    }
}
