using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The narrator's voice: reads the quest's texts aloud in the current language, as a queue of lines, each said
/// by the narrator or by Mihail Sturdza (VoiceRole).
///
/// A line is played from its recording (NarrationLibrary: ElevenLabs voices recorded in the Editor) when one
/// exists for its exact text; otherwise it falls back to Android's on-device TextToSpeech (and logs a warning).
/// The fallback asks for Google's engine (com.google.android.tts) because Samsung's default engine has no
/// Romanian voice (phone check, 20 Nov 2026); if the language's voice data is missing, the engine can download
/// it (OpenVoiceInstall). In the Editor the fallback isn't spoken: the text is logged and "speaking" lasts as
/// long as reading it would. The voice can be switched off (remembered in PlayerPrefs "Narrator.On").
/// </summary>
public class Narrator : MonoBehaviour
{
    public enum VoiceState
    {
        Starting,      // the engine is starting
        Ready,         // the current language has a voice (recordings, or the engine's)
        MissingData,   // the engine knows the language but its voice isn't downloaded
        NotSupported,  // no voice for the language
        Failed,        // no engine
    }

    enum Part
    {
        None,
        Clip,           // a recording is playing
        Engine,         // Android's voice is reading
        WaitingEngine,  // Android's voice is still starting
        Pause,          // the short pause between two lines
    }

    const string GoogleEngine = "com.google.android.tts";
    const string OnKey = "Narrator.On";

    [Tooltip("The recorded narration (ElevenLabs). Lines without a recording use Android's voice.")]
    [SerializeField] NarrationLibrary library;

    [Tooltip("Seconds of silence between two lines (e.g. a title and its text).")]
    [SerializeField] float pauseBetweenLines = 0.35f;

#pragma warning disable 0414 // one is used only on Android, the other only in the Editor
    [Tooltip("Android's voice: speaking speed (1 = normal).")]
    [SerializeField] float speechRate = 0.95f;

    [Tooltip("Editor only: words per second for the simulated speech of lines without a recording.")]
    [SerializeField] float editorWordsPerSecond = 2.6f;
#pragma warning restore 0414

    /// <summary>Raised when a text finishes (or is stopped / skipped).</summary>
    public event Action Finished;

    /// <summary>Raised when the voice state changes (e.g. the engine is ready, the language changed).</summary>
    public event Action StateChanged;

    /// <summary>Ready when the current language is recorded; else the state of Android's voice.</summary>
    public VoiceState State =>
        library != null && library.Covers(Localization.Current) ? VoiceState.Ready : engineState;

    /// <summary>
    /// The user's voice switch. Turning it off pauses what is being read (Resume carries on from there);
    /// while it is off, new texts aren't read.
    /// </summary>
    public bool On
    {
        get => PlayerPrefs.GetInt(OnKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(OnKey, value ? 1 : 0);
            PlayerPrefs.Save();
            if (!value) Pause();
            StateChanged?.Invoke();
        }
    }

    /// <summary>True while a text is being read (also while it is paused).</summary>
    public bool IsSpeaking => speaking;

    /// <summary>True when a text was paused halfway (Resume carries on from where it stopped).</summary>
    public bool IsPaused => paused;

    VoiceState engineState = VoiceState.Starting;
    AudioSource source;
    readonly List<NarrationLine> queue = new List<NarrationLine>();
    int lineIndex;
    Part part;
    bool speaking;
    float partStartedAt;
    float partEndsAt;     // the pause's end; in the Editor also the simulated speech's end
    bool paused;
    float pausedRemaining; // a timed part (the pause between lines, the Editor's simulated speech) when paused
    int utterance;

#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaObject tts;
    volatile int initStatus = 1; // 1 = waiting; 0 = TextToSpeech.SUCCESS; -1 = error
    bool initHandled;

    class InitListener : AndroidJavaProxy
    {
        readonly Narrator owner;

        public InitListener(Narrator owner) : base("android.speech.tts.TextToSpeech$OnInitListener") =>
            this.owner = owner;

        // Called on Android's thread: only store the result, Update() handles it.
        public void onInit(int status) => owner.initStatus = status;
    }
#endif

    void Awake()
    {
        source = GetComponent<AudioSource>();
        if (source == null) source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f; // a voice in the ear, not a sound in the scene
    }

    void OnEnable() => Localization.Changed += ApplyLanguage;

    void OnDisable() => Localization.Changed -= ApplyLanguage;

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, new InitListener(this),
                GoogleEngine);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Narrator] TextToSpeech failed to start: " + e.Message);
            SetEngineState(VoiceState.Failed);
        }
#else
        SetEngineState(VoiceState.Ready);
#endif
        if (library == null) Debug.LogWarning("[Narrator] No narration library: Android's voice reads everything.");
    }

    void OnDestroy()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (tts != null)
        {
            tts.Call("stop");
            tts.Call("shutdown");
            tts.Dispose();
            tts = null;
        }
#endif
    }

    void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!initHandled && initStatus != 1)
        {
            initHandled = true;
            if (initStatus == 0)
            {
                tts.Call<int>("setSpeechRate", speechRate);
                SetEngineLanguage(); // not ApplyLanguage: a line may be waiting for the engine
            }
            else
            {
                Debug.LogWarning("[Narrator] TextToSpeech init failed: " + initStatus);
                SetEngineState(VoiceState.Failed);
            }
            if (part == Part.WaitingEngine && !paused) SpeakWithEngine(queue[lineIndex].Text);
        }
#endif
        if (paused) return;
        float now = Time.unscaledTime;
        switch (part)
        {
            case Part.Clip:
                if (!source.isPlaying && now - partStartedAt > 0.1f) LineDone();
                break;
            case Part.Engine:
#if UNITY_ANDROID && !UNITY_EDITOR
                // isSpeaking can still be false just after speak(): give it a moment before calling it done.
                if (tts == null || !tts.Call<bool>("isSpeaking") && now - partStartedAt > 0.8f) LineDone();
#else
                if (now >= partEndsAt) LineDone();
#endif
                break;
            case Part.Pause:
                if (now >= partEndsAt) NextLine();
                break;
        }
    }

    /// <summary>Reads the text aloud with the narrator's voice (see the other overload).</summary>
    public void Speak(string text) => Speak(VoiceRole.Narrator, text);

    /// <summary>
    /// Reads the parts aloud one after the other (e.g. a title, then its text), all with this role's voice,
    /// replacing whatever was being read or paused. Empty parts are skipped. Reads nothing when the voice is off.
    /// </summary>
    public void Speak(VoiceRole role, params string[] parts)
    {
        Stop(); // a new text: whatever was paused is forgotten, even while the voice is off
        if (!On || parts == null) return;
        foreach (var text in parts)
            if (!string.IsNullOrWhiteSpace(text)) queue.Add(new NarrationLine(role, text));
        if (queue.Count == 0) return;
        speaking = true;
        lineIndex = -1;
        NextLine();
    }

    /// <summary>Stops reading (the Skip button, a new screen). Raises Finished if something was being read.</summary>
    public void Stop()
    {
        if (source != null) source.Stop();
#if UNITY_ANDROID && !UNITY_EDITOR
        if (tts != null && initHandled && part == Part.Engine) tts.Call<int>("stop");
#endif
        queue.Clear();
        part = Part.None;
        paused = false;
        if (speaking) EndSpeaking();
    }

    /// <summary>Pauses what is being read, where it is (the voice switched off). Resume carries on.</summary>
    public void Pause()
    {
        if (!speaking || paused) return;
        paused = true;
        pausedRemaining = Mathf.Max(0f, partEndsAt - Time.unscaledTime);
        switch (part)
        {
            case Part.Clip:
                source.Pause();
                break;
#if UNITY_ANDROID && !UNITY_EDITOR
            case Part.Engine:
                tts.Call<int>("stop"); // Android's voice can't pause: the line starts again on Resume
                break;
#endif
        }
    }

    /// <summary>Carries on from where Pause stopped (the voice switched on again). Returns false if nothing
    /// was paused.</summary>
    public bool Resume()
    {
        if (!paused || !speaking || !On) return false;
        paused = false;
        float now = Time.unscaledTime;
        switch (part)
        {
            case Part.Clip:
                source.UnPause();
                partStartedAt = now; // isPlaying may lag for a moment after UnPause
                break;
            case Part.Pause:
                partEndsAt = now + pausedRemaining;
                break;
            case Part.Engine:
#if UNITY_ANDROID && !UNITY_EDITOR
                SpeakWithEngine(queue[lineIndex].Text);
#else
                partEndsAt = now + pausedRemaining;
#endif
                break;
            case Part.WaitingEngine:
#if UNITY_ANDROID && !UNITY_EDITOR
                if (initHandled) SpeakWithEngine(queue[lineIndex].Text);
#endif
                break;
        }
        return true;
    }

    /// <summary>Opens the engine's screen to download the current language's voice (when it is missing).</summary>
    public void OpenVoiceInstall()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var intent = new AndroidJavaObject("android.content.Intent",
                "android.speech.tts.engine.INSTALL_TTS_DATA");
            intent.Call<AndroidJavaObject>("setPackage", GoogleEngine);
            intent.Call<AndroidJavaObject>("addFlags", 0x10000000); // FLAG_ACTIVITY_NEW_TASK
            activity.Call("startActivity", intent);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Narrator] Couldn't open the voice download: " + e.Message);
        }
#endif
    }

    // The queue
    void NextLine()
    {
        lineIndex++;
        if (lineIndex >= queue.Count)
        {
            part = Part.None;
            EndSpeaking();
            return;
        }
        var line = queue[lineIndex];
        partStartedAt = Time.unscaledTime;
        var clip = library != null ? library.Find(line.Role, Localization.Current, line.Text) : null;
        if (clip != null)
        {
            source.clip = clip;
            source.Play();
            part = Part.Clip;
            return;
        }
        Debug.LogWarning($"[Narrator] No recording ({line.Role}, {Localization.Current}): {Short(line.Text)}");
        SpeakWithEngine(line.Text);
    }

    /// The line is over: a short pause, then the next one (none after the last line).
    void LineDone()
    {
        if (lineIndex >= queue.Count - 1)
        {
            part = Part.None;
            EndSpeaking();
            return;
        }
        part = Part.Pause;
        partEndsAt = Time.unscaledTime + pauseBetweenLines;
    }

    /// Android's voice reads a line that has no recording (in the Editor: logged and simulated).
    void SpeakWithEngine(string text)
    {
        partStartedAt = Time.unscaledTime;
        utterance++;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (engineState == VoiceState.Starting)
        {
            part = Part.WaitingEngine;
            return;
        }
        if (engineState != VoiceState.Ready || tts == null)
        {
            LineDone(); // no voice for it: skip the line
            return;
        }
        // An empty Bundle, not null: the JNI method lookup needs a typed argument.
        using var parameters = new AndroidJavaObject("android.os.Bundle");
        int result = tts.Call<int>("speak", text, 0 /* QUEUE_FLUSH */, parameters, "arch" + utterance);
        if (result != 0)
        {
            Debug.LogWarning("[Narrator] speak() failed: " + result);
            LineDone();
            return;
        }
#else
        int words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
        partEndsAt = Time.unscaledTime + Mathf.Max(1f, words / Mathf.Max(0.5f, editorWordsPerSecond));
        Debug.Log($"[Narrator] ({Localization.LanguageTag}) {text}");
#endif
        part = Part.Engine;
    }

    void EndSpeaking()
    {
        speaking = false;
        Finished?.Invoke();
    }

    static string Short(string text) => text.Length <= 60 ? text : text.Substring(0, 60) + "...";

    /// The language changed: stop reading, set Android's voice to it; the recordings may cover it or not.
    void ApplyLanguage()
    {
        Stop();
        SetEngineLanguage();
        StateChanged?.Invoke();
    }

    void SetEngineLanguage()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (tts == null || !initHandled || initStatus != 0) return;
        using var localeClass = new AndroidJavaClass("java.util.Locale");
        using var locale = localeClass.CallStatic<AndroidJavaObject>("forLanguageTag", Localization.LanguageTag);
        int result = tts.Call<int>("setLanguage", locale);
        // LANG_MISSING_DATA = -1, LANG_NOT_SUPPORTED = -2, else available.
        var state = result == -1 ? VoiceState.MissingData
            : result == -2 ? VoiceState.NotSupported
            : VoiceState.Ready;
        Debug.Log($"[Narrator] Voice {Localization.LanguageTag}: {state} (setLanguage {result}); recordings: " +
                  (library != null && library.Covers(Localization.Current) ? "yes" : "no"));
        SetEngineState(state);
#endif
    }

    void SetEngineState(VoiceState state)
    {
        if (engineState == state) return;
        engineState = state;
        StateChanged?.Invoke();
    }
}
