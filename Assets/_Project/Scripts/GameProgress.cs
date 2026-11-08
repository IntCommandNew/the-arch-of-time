using UnityEngine;

/// <summary>The two ways to play. Each has its own progress; nothing is shared between them.</summary>
public enum GameMode
{
    InCity, // at the real places in Iasi (not built yet)
    Free,   // the academy placed anywhere, pieces appear around the user
}

/// <summary>
/// Saved progress, kept separately for each mode (PlayerPrefs). For now: which of the arch's pieces
/// have been collected, stored as one bit per piece, so the in-city mode can later tell which places
/// are done, not only how many.
/// </summary>
public static class GameProgress
{
    /// <summary>The arch is made of 4 pieces: left column, right column, the arc and the top beam.</summary>
    public const int PieceCount = 4;

    const int AllPieces = (1 << PieceCount) - 1;

    public static bool IsCollected(GameMode mode, int piece) =>
        piece >= 0 && piece < PieceCount && (Pieces(mode) & (1 << piece)) != 0;

    public static int CollectedCount(GameMode mode)
    {
        int count = 0;
        for (int i = 0; i < PieceCount; i++)
            if (IsCollected(mode, i)) count++;
        return count;
    }

    /// <summary>The lowest piece not collected yet, or -1 when all are collected.</summary>
    public static int NextPiece(GameMode mode)
    {
        for (int i = 0; i < PieceCount; i++)
            if (!IsCollected(mode, i)) return i;
        return -1;
    }

    public static void MarkCollected(GameMode mode, int piece)
    {
        if (piece < 0 || piece >= PieceCount) return;
        PlayerPrefs.SetInt(Key(mode), Pieces(mode) | (1 << piece));
        PlayerPrefs.Save();
    }

    public static void ResetPieces(GameMode mode)
    {
        PlayerPrefs.DeleteKey(Key(mode));
        PlayerPrefs.Save();
    }

    static int Pieces(GameMode mode) => PlayerPrefs.GetInt(Key(mode), 0) & AllPieces;

    static string Key(GameMode mode) => "Progress." + mode + ".Pieces";
}
