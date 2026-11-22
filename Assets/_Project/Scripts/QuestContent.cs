using System;
using UnityEngine;

/// <summary>
/// The quest's texts in both languages, read from Assets/_Project/Content/QuestContent.json: the greeting,
/// the tutorial seal, the four stops (riddle, knowledge, quiz, secret, the real place's coordinates) and the
/// finale. The stops are in route order; stop i gives arch piece i (GameProgress order).
/// In every quiz question the first answer in the file is the right one; the quiz shuffles them.
/// </summary>
[Serializable]
public class QuestContent
{
    public GeoPoint academySite;
    public Greeting greeting;
    public SecretCard tutorialSeal;
    public Stop[] stops;
    public Finale finale;

    public static QuestContent Parse(TextAsset json)
    {
        if (json == null) return null;
        var content = JsonUtility.FromJson<QuestContent>(json.text);
        if (content?.stops == null || content.stops.Length != GameProgress.PieceCount)
            Debug.LogError($"[QuestContent] Expected {GameProgress.PieceCount} stops in {json.name}.");
        return content;
    }

    [Serializable]
    public class GeoPoint
    {
        public double latitude;
        public double longitude;
    }

    [Serializable]
    public class Greeting
    {
        public LocalizedText speaker;
        public LocalizedText title;
        public LocalizedText text;
    }

    [Serializable]
    public class SecretCard
    {
        public LocalizedText intro;   // only the tutorial seal has one
        public LocalizedText title;
        public LocalizedText text;
    }

    [Serializable]
    public class Stop
    {
        public string id;
        public int piece;
        public LocalizedText place;
        public double latitude;       // the REAL place (the map in the Anywhere mode)
        public double longitude;
        public LocalizedText chapter;
        public LocalizedText pieceName;
        public LocalizedText riddle;
        public LocalizedText[] knowledge;
        public Question[] quiz;
        public SecretCard secret;
    }

    [Serializable]
    public class Question
    {
        public LocalizedText question;
        public LocalizedText[] answers;   // answers[0] is the right one
        public LocalizedText explanation;
    }

    [Serializable]
    public class Finale
    {
        public LocalizedText intro;
        public FinaleStep[] steps;
        public LocalizedText legacy;
        public SecretCard letter;
    }

    [Serializable]
    public class FinaleStep
    {
        public string year;
        public LocalizedText text;
    }
}
