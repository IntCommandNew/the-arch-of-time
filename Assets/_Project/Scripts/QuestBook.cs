using UnityEngine;

/// <summary>
/// Loads the quest content (QuestContent.json) and the UI strings (UiText.json) when the app starts, so every
/// script reads the same copy. On App, before AppController (DefaultExecutionOrder).
/// </summary>
[DefaultExecutionOrder(-100)]
public class QuestBook : MonoBehaviour
{
    [SerializeField] TextAsset questContent;
    [SerializeField] TextAsset uiText;

    /// <summary>The quest's texts (null until Awake, or when the file is missing).</summary>
    public static QuestContent Content { get; private set; }

    void Awake()
    {
        Localization.Load(uiText);
        Content = QuestContent.Parse(questContent);
        if (Content == null) Debug.LogError("[QuestBook] No quest content.");
    }
}
