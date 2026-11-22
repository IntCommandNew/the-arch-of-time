using UnityEngine;

/// <summary>The two ways to play. Each has its own progress; nothing is shared between them.</summary>
public enum GameMode
{
    InCity, // at the real places in Iasi
    Free,   // the academy placed anywhere, pieces appear around the user
}

/// <summary>Where the player is in the quest (derived from the saved progress, see GameProgress.Phase).</summary>
public enum QuestPhase
{
    Welcome,    // the greeting and the tutorial seal
    Riddle,     // find the current stop (In Iasi: walk there; Anywhere: pick it on the map)
    Knowledge,  // the piece is collected: read the knowledge cards
    Quiz,       // answer the questions
    Finale,     // all four stops done: the arch rises
    Done,       // the finale was played
}

/// <summary>
/// Saved progress, kept separately for each mode (PlayerPrefs "Progress.(mode).(what)"):
/// - Pieces: one bit per arch piece (0 left column, 1 right column, 2 arc, 3 top beam), as before.
/// - Welcome: the greeting was seen. Knowledge, QuizDone, QuizPerfect: one bit per stop.
/// - Seals: one bit per seal (0 the tutorial seal, 1 + i the seal of stop i).
/// - Misses.(i), Score.(i): the Anywhere mode's map guesses and score for stop i. Finale: the finale was played.
/// The stop being played is the first one not finished (piece + knowledge + quiz), in route order.
/// </summary>
public static class GameProgress
{
    /// <summary>The arch is made of 4 pieces: left column, right column, the arc and the top beam.</summary>
    public const int PieceCount = 4;

    /// <summary>The tutorial seal plus one seal per stop.</summary>
    public const int SealCount = PieceCount + 1;

    /// <summary>The best score for one stop (Anywhere mode).</summary>
    public const int MaxStopScore = 100;

    const int AllPieces = (1 << PieceCount) - 1;
    const int AllSeals = (1 << SealCount) - 1;

    // Pieces

    public static bool IsCollected(GameMode mode, int piece) => HasBit(mode, "Pieces", piece, PieceCount);

    public static int CollectedCount(GameMode mode) => CountBits(Bits(mode, "Pieces") & AllPieces);

    /// <summary>The lowest piece not collected yet, or -1 when all are collected.</summary>
    public static int NextPiece(GameMode mode)
    {
        for (int i = 0; i < PieceCount; i++)
            if (!IsCollected(mode, i)) return i;
        return -1;
    }

    public static void MarkCollected(GameMode mode, int piece) => SetBit(mode, "Pieces", piece, PieceCount);

    // Quest steps

    public static bool WelcomeDone(GameMode mode) => Bits(mode, "Welcome") != 0;

    public static void MarkWelcomeDone(GameMode mode) => SetInt(mode, "Welcome", 1);

    public static bool KnowledgeRead(GameMode mode, int stop) => HasBit(mode, "Knowledge", stop, PieceCount);

    public static void MarkKnowledgeRead(GameMode mode, int stop) => SetBit(mode, "Knowledge", stop, PieceCount);

    public static int KnowledgeCount(GameMode mode) => CountBits(Bits(mode, "Knowledge") & AllPieces);

    public static bool QuizDone(GameMode mode, int stop) => HasBit(mode, "QuizDone", stop, PieceCount);

    public static bool QuizPerfect(GameMode mode, int stop) => HasBit(mode, "QuizPerfect", stop, PieceCount);

    public static void MarkQuizDone(GameMode mode, int stop, bool perfect)
    {
        SetBit(mode, "QuizDone", stop, PieceCount);
        if (perfect) SetBit(mode, "QuizPerfect", stop, PieceCount);
    }

    public static bool StopFinished(GameMode mode, int stop) =>
        IsCollected(mode, stop) && KnowledgeRead(mode, stop) && QuizDone(mode, stop);

    /// <summary>The stop being played (route order), or -1 when all four are finished.</summary>
    public static int CurrentStop(GameMode mode)
    {
        for (int i = 0; i < PieceCount; i++)
            if (!StopFinished(mode, i)) return i;
        return -1;
    }

    public static bool FinaleDone(GameMode mode) => Bits(mode, "Finale") != 0;

    public static void MarkFinaleDone(GameMode mode) => SetInt(mode, "Finale", 1);

    /// <summary>What the player does next in this mode.</summary>
    public static QuestPhase Phase(GameMode mode)
    {
        if (!WelcomeDone(mode)) return QuestPhase.Welcome;
        int stop = CurrentStop(mode);
        if (stop < 0) return FinaleDone(mode) ? QuestPhase.Done : QuestPhase.Finale;
        if (!IsCollected(mode, stop)) return QuestPhase.Riddle;
        if (!KnowledgeRead(mode, stop)) return QuestPhase.Knowledge;
        return QuestPhase.Quiz;
    }

    // Seals (0 = the tutorial seal, 1 + i = stop i's seal)

    public static bool SealFound(GameMode mode, int seal) => HasBit(mode, "Seals", seal, SealCount);

    public static void MarkSealFound(GameMode mode, int seal) => SetBit(mode, "Seals", seal, SealCount);

    public static int SealsFoundCount(GameMode mode) => CountBits(Bits(mode, "Seals") & AllSeals);

    /// <summary>A seal earned but not collected yet (the tutorial one, or a perfect quiz's), or -1.</summary>
    public static int PendingSeal(GameMode mode)
    {
        if (WelcomeDone(mode) && !SealFound(mode, 0)) return 0;
        for (int i = 0; i < PieceCount; i++)
            if (QuizPerfect(mode, i) && !SealFound(mode, 1 + i)) return 1 + i;
        return -1;
    }

    // Map guesses and score (Anywhere mode)

    public static int Misses(GameMode mode, int stop) => PlayerPrefs.GetInt(Key(mode, "Misses." + stop), 0);

    public static void AddMiss(GameMode mode, int stop) => SetInt(mode, "Misses." + stop, Misses(mode, stop) + 1);

    /// <summary>The score earned for this stop, or -1 when it isn't found on the map yet.</summary>
    public static int Score(GameMode mode, int stop) => PlayerPrefs.GetInt(Key(mode, "Score." + stop), -1);

    public static void SetScore(GameMode mode, int stop, int score) => SetInt(mode, "Score." + stop, score);

    public static int TotalScore(GameMode mode)
    {
        int total = 0;
        for (int i = 0; i < PieceCount; i++)
            total += Mathf.Max(0, Score(mode, i));
        return total;
    }

    /// <summary>
    /// The score for finding a stop on the map after this many misses: 100, -10 per miss, at most 60 once the
    /// big circle is shown (3 misses), 30 with the small circle (6), 10 when the place is revealed (9).
    /// </summary>
    public static int ScoreAfterMisses(int misses)
    {
        int score = MaxStopScore - 10 * misses;
        if (misses >= 9) return 10;
        if (misses >= 6) return Mathf.Min(score, 30);
        if (misses >= 3) return Mathf.Min(score, 60);
        return score;
    }

    /// <summary>"Start over": forgets everything in this mode (the language is kept).</summary>
    public static void ResetAll(GameMode mode)
    {
        foreach (var what in new[] { "Pieces", "Welcome", "Knowledge", "QuizDone", "QuizPerfect", "Seals", "Finale" })
            PlayerPrefs.DeleteKey(Key(mode, what));
        for (int i = 0; i < PieceCount; i++)
        {
            PlayerPrefs.DeleteKey(Key(mode, "Misses." + i));
            PlayerPrefs.DeleteKey(Key(mode, "Score." + i));
        }
        PlayerPrefs.Save();
    }

    /// <summary>Kept for the old callers: starting over forgets the whole quest, not only the pieces.</summary>
    public static void ResetPieces(GameMode mode) => ResetAll(mode);

    // PlayerPrefs helpers

    static string Key(GameMode mode, string what) => "Progress." + mode + "." + what;

    static int Bits(GameMode mode, string what) => PlayerPrefs.GetInt(Key(mode, what), 0);

    static bool HasBit(GameMode mode, string what, int index, int count) =>
        index >= 0 && index < count && (Bits(mode, what) & (1 << index)) != 0;

    static void SetBit(GameMode mode, string what, int index, int count)
    {
        if (index < 0 || index >= count) return;
        SetInt(mode, what, Bits(mode, what) | (1 << index));
    }

    static void SetInt(GameMode mode, string what, int value)
    {
        PlayerPrefs.SetInt(Key(mode, what), value);
        PlayerPrefs.Save();
    }

    static int CountBits(int bits)
    {
        int count = 0;
        for (; bits != 0; bits &= bits - 1) count++;
        return count;
    }
}
