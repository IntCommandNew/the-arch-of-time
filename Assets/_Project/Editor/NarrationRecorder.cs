using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records the narration with ElevenLabs (Tools > The Arch of Time > Narration ...): every line the quest can
/// speak (QuestFlow.SpokenLines), in English and Romanian, with the narrator's or Sturdza's voice
/// (NarrationLibrary's voice IDs and model). One MP3 per line in Assets/_Project/Audio/Narration/(voice)/(lang)/,
/// named by a hash of voice + language + text, so a changed text gets a new recording and the old one is left
/// unused (listed by Report, removed by RemoveUnused). Only missing lines are recorded.
///
/// The API key is read from (user folder)/.thearchoftime/elevenlabs-key.txt: never put it in the project.
/// Recording runs in the background of the Editor (don't recompile meanwhile); Status shows the progress.
/// </summary>
public static class NarrationRecorder
{
    public const string LibraryPath = "Assets/_Project/Audio/NarrationLibrary.asset";
    public const string AudioFolder = "Assets/_Project/Audio/Narration";
    const string ContentPath = "Assets/_Project/Content/QuestContent.json";
    const string UiTextPath = "Assets/_Project/Content/UiText.json";
    const string Api = "https://api.elevenlabs.io";
    const string OutputFormat = "mp3_44100_64";
    const int ParallelRequests = 3;
    const int Attempts = 3;

    static string KeyFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".thearchoftime", "elevenlabs-key.txt");

    /// <summary>What the recorder is doing (for the Unity CLI: return NarrationRecorder.Status;).</summary>
    public static string Status { get; private set; } = "Idle";

    public static bool Running { get; private set; }

    class Line
    {
        public VoiceRole role;
        public Language language;
        public string text;
        public string path;
    }

    [Serializable]
    class SpeechRequest
    {
        public string text;
        public string model_id;
        public string language_code;
    }

    [Serializable]
    class Subscription
    {
        public int character_count;
        public int character_limit;
    }

    // Menu

    [MenuItem("Tools/The Arch of Time/Narration: report")]
    public static string Report()
    {
        var lines = CollectLines();
        int recorded = 0, missingChars = 0;
        var missing = new List<Line>();
        foreach (var line in lines)
        {
            if (File.Exists(line.path)) recorded++;
            else
            {
                missing.Add(line);
                missingChars += line.text.Length;
            }
        }
        var unused = UnusedFiles(lines);
        var report = new StringBuilder();
        report.AppendLine($"Narration: {lines.Count} lines, {recorded} recorded, {missing.Count} missing " +
                          $"({missingChars} characters), {unused.Count} unused recordings.");
        foreach (var line in missing) report.AppendLine($"  missing {line.role} {line.language}: {Short(line.text)}");
        foreach (var file in unused) report.AppendLine("  unused " + file);
        Debug.Log(report.ToString());
        return report.ToString();
    }

    [MenuItem("Tools/The Arch of Time/Narration: record the missing lines")]
    public static void RecordMissingFromMenu() => RecordMissing(int.MaxValue);

    /// <summary>
    /// Starts recording the missing lines (at most maxLines of them, for a trial), then updates the library.
    /// Returns at once; watch Status.
    /// </summary>
    public static string RecordMissing(int maxLines)
    {
        if (Running) return Status;
        _ = RecordAsync(maxLines);
        return Status;
    }

    [MenuItem("Tools/The Arch of Time/Narration: update the library")]
    public static string UpdateLibrary()
    {
        var library = LoadOrCreateLibrary();
        var lines = CollectLines();
        library.recordings.Clear();
        int missing = 0;
        foreach (var line in lines)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(line.path);
            if (clip == null)
            {
                missing++;
                continue;
            }
            library.recordings.Add(new NarrationLibrary.Recording
            {
                role = line.role,
                language = line.language,
                text = line.text,
                clip = clip,
            });
        }
        library.Invalidate();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        string result = $"Library: {library.recordings.Count} recordings, {missing} lines without one.";
        Debug.Log("[NarrationRecorder] " + result);
        return result;
    }

    [MenuItem("Tools/The Arch of Time/Narration: remove unused recordings")]
    public static string RemoveUnused()
    {
        var unused = UnusedFiles(CollectLines());
        foreach (var file in unused) AssetDatabase.DeleteAsset(file);
        UpdateLibrary();
        return $"Removed {unused.Count} unused recordings.";
    }

    // Recording

    static async Task RecordAsync(int maxLines)
    {
        Running = true;
        try
        {
            if (!File.Exists(KeyFile))
            {
                Status = "No API key: save it in " + KeyFile;
                return;
            }
            string key = File.ReadAllText(KeyFile).Trim();
            var library = LoadOrCreateLibrary();
            var jobs = new List<Line>();
            int characters = 0;
            foreach (var line in CollectLines())
            {
                if (File.Exists(line.path) || jobs.Count >= maxLines) continue;
                jobs.Add(line);
                characters += line.text.Length;
            }
            if (jobs.Count == 0)
            {
                Status = "Nothing to record. " + UpdateLibrary();
                return;
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
            http.DefaultRequestHeaders.Add("xi-api-key", key);

            // Don't start what the plan can't finish (a credit costs at most one character).
            var subscription = JsonUtility.FromJson<Subscription>(
                await http.GetStringAsync(Api + "/v1/user/subscription"));
            int left = subscription.character_limit - subscription.character_count;
            if (characters > left)
            {
                Status = $"Not enough credits: {characters} characters to record, {left} left this month.";
                return;
            }

            int done = 0, failed = 0;
            var errors = new List<string>();
            Status = $"Recording 0 / {jobs.Count} ({characters} characters, {left} credits left)";
            using var gate = new SemaphoreSlim(ParallelRequests);
            var tasks = new List<Task>();
            foreach (var job in jobs)
            {
                await gate.WaitAsync();
                tasks.Add(RecordOne(http, library, job).ContinueWith(task =>
                {
                    gate.Release();
                    if (task.Result != null)
                    {
                        failed++;
                        errors.Add(task.Result);
                    }
                    else done++;
                    Status = $"Recording {done + failed} / {jobs.Count} (failed {failed})";
                }, TaskScheduler.FromCurrentSynchronizationContext()));
            }
            await Task.WhenAll(tasks);

            AssetDatabase.Refresh();
            string libraryResult = UpdateLibrary();
            Status = $"Done: {done} recorded, {failed} failed. {libraryResult}";
            foreach (var error in errors) Debug.LogWarning("[NarrationRecorder] " + error);
            Debug.Log("[NarrationRecorder] " + Status);
        }
        catch (Exception e)
        {
            Status = "Failed: " + e.Message;
            Debug.LogException(e);
        }
        finally
        {
            Running = false;
        }
    }

    /// Records one line; returns null when it worked, else the error.
    static async Task<string> RecordOne(HttpClient http, NarrationLibrary library, Line line)
    {
        var request = new SpeechRequest
        {
            text = line.text,
            model_id = library.modelId,
            language_code = line.language == Language.Romanian ? "ro" : "en",
        };
        string url = $"{Api}/v1/text-to-speech/{library.VoiceId(line.role)}?output_format={OutputFormat}";
        string error = null;
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                using var body = new StringContent(JsonUtility.ToJson(request), Encoding.UTF8, "application/json");
                using var response = await http.PostAsync(url, body);
                if (response.IsSuccessStatusCode)
                {
                    byte[] audio = await response.Content.ReadAsByteArrayAsync();
                    Directory.CreateDirectory(Path.GetDirectoryName(line.path));
                    File.WriteAllBytes(line.path, audio);
                    return null;
                }
                string answer = await response.Content.ReadAsStringAsync();
                error = $"HTTP {(int)response.StatusCode} for {line.role} {line.language} '{Short(line.text)}': " +
                        (answer.Length > 300 ? answer.Substring(0, 300) : answer);
                // Too many requests or a server error: wait and try again; anything else won't get better.
                int code = (int)response.StatusCode;
                if (code != 429 && code < 500) return error;
            }
            catch (Exception e)
            {
                error = $"{e.GetType().Name} for '{Short(line.text)}': {e.Message}";
            }
            await Task.Delay(2000 * attempt);
        }
        return error;
    }

    // Lines and files

    static List<Line> CollectLines()
    {
        Localization.Load(AssetDatabase.LoadAssetAtPath<TextAsset>(UiTextPath));
        var content = QuestContent.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(ContentPath));
        var lines = new List<Line>();
        var seen = new HashSet<string>();
        var spoken = new List<NarrationLine>();
        foreach (Language language in Enum.GetValues(typeof(Language)))
        {
            spoken.Clear();
            QuestFlow.SpokenLines(content, language, spoken);
            foreach (var line in spoken)
            {
                string text = line.Text.Trim();
                string key = NarrationLibrary.Key(line.Role, language, text);
                if (!seen.Add(key)) continue; // the same line said twice (e.g. an answer used in two places)
                lines.Add(new Line
                {
                    role = line.Role,
                    language = language,
                    text = text,
                    path = PathOf(key, line.Role, language),
                });
            }
        }
        return lines;
    }

    static string PathOf(string key, VoiceRole role, Language language)
    {
        using var sha = SHA1.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        var name = new StringBuilder();
        for (int i = 0; i < 8; i++) name.Append(hash[i].ToString("x2"));
        string lang = language == Language.Romanian ? "ro" : "en";
        return $"{AudioFolder}/{role}/{lang}/{name}.mp3";
    }

    static List<string> UnusedFiles(List<Line> lines)
    {
        var used = new HashSet<string>();
        foreach (var line in lines) used.Add(line.path);
        var unused = new List<string>();
        if (!Directory.Exists(AudioFolder)) return unused;
        foreach (string file in Directory.GetFiles(AudioFolder, "*.mp3", SearchOption.AllDirectories))
        {
            string path = file.Replace('\\', '/');
            if (!used.Contains(path)) unused.Add(path);
        }
        return unused;
    }

    static NarrationLibrary LoadOrCreateLibrary()
    {
        var library = AssetDatabase.LoadAssetAtPath<NarrationLibrary>(LibraryPath);
        if (library != null) return library;
        Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
        library = ScriptableObject.CreateInstance<NarrationLibrary>();
        AssetDatabase.CreateAsset(library, LibraryPath);
        AssetDatabase.SaveAssets();
        return library;
    }

    static string Short(string text) => text.Length <= 50 ? text : text.Substring(0, 50) + "...";
}

/// <summary>
/// Import settings for the recorded narration: mono, Vorbis, 22 kHz, decompressed when played (not loaded with
/// the scene), so twenty minutes of speech stay small in the APK and in memory.
/// </summary>
public class NarrationImport : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith(NarrationRecorder.AudioFolder + "/", StringComparison.Ordinal)) return;
        var importer = (AudioImporter)assetImporter;
        importer.forceToMono = true;
        importer.loadInBackground = false;
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.CompressedInMemory;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = 0.5f;
        settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
        settings.sampleRateOverride = 22050;
        settings.preloadAudioData = false;
        importer.defaultSampleSettings = settings;
    }
}
