using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>One stop of the route in the progress view.</summary>
public struct ProgressStop
{
    public string label;     // "Stop 1"
    public string place;     // the place's name, or "Not found yet"
    public string status;    // what was done there
    public int state;        // 0 not found yet, 1 the riddle being solved, 2 done
    public bool canReread;   // its knowledge can be read again
}

/// <summary>
/// The quest's overlays (part of AppUI): the story sheet (greeting, riddle, knowledge, secret, finale), the quiz,
/// the progress view and the finale's year. They are shown over the current screen; while one is open the
/// screen under it is hidden (the camera view stays visible above a sheet).
/// </summary>
public partial class AppUI
{
    /// <summary>The sheet's main button ("Continue", "Open the map"...).</summary>
    public event Action SheetPrimaryClicked;

    /// <summary>The voice switch on the sheet or the quiz.</summary>
    public event Action VoiceClicked;

    /// <summary>An answer of the quiz was chosen (its index on screen).</summary>
    public event Action<int> QuizAnswerChosen;

    /// <summary>The quiz's button after an answer ("Next question", "See the result").</summary>
    public event Action QuizNextClicked;

    /// <summary>The progress view: "Back", "Read again" (a stop), "Start over" confirmed.</summary>
    public event Action ProgressBackClicked;
    public event Action<int> ProgressRereadClicked;
    public event Action StartOverConfirmed;

    public const int QuizAnswerSlots = 3;
    public const int ProgressStopSlots = 4;

    const long SheetEnterDelayMs = 30;
    const long AnswerFirstDelayMs = 140;
    const long AnswerStepMs = 110;

    VisualElement questSheet;
    VisualElement sheet;
    VisualElement sheetSeal;
    Label sheetKicker;
    Label sheetTitle;
    Label sheetBody;
    Label sheetPage;
    ScrollView sheetScroll;
    Button sheetPrimary;
    Button sheetVoice;

    VisualElement questQuiz;
    VisualElement quizSheet;
    Label quizCounter;
    Label quizQuestion;
    Label quizFeedback;
    readonly Button[] quizAnswers = new Button[QuizAnswerSlots];
    readonly Label[] quizAnswerTexts = new Label[QuizAnswerSlots];
    Button quizNext;
    Button quizVoice;
    bool quizAnswered;

    VisualElement questProgress;
    ArchEmblem progressArch;
    Label progressPieces;
    Label progressSeals;
    Label progressScore;
    readonly VisualElement[] stopRows = new VisualElement[ProgressStopSlots];
    readonly Label[] stopLabels = new Label[ProgressStopSlots];
    readonly Label[] stopPlaces = new Label[ProgressStopSlots];
    readonly Label[] stopStatuses = new Label[ProgressStopSlots];
    readonly Button[] stopRereads = new Button[ProgressStopSlots];
    VisualElement confirmStartOver;

    SealRow progressSealRow;

    Label finaleYear;

    /// <summary>True while a sheet, the quiz or the progress view covers the current screen.</summary>
    public bool BaseCovered =>
        IsOpen(questSheet) || IsOpen(questQuiz) || IsOpen(questProgress) || IsOpen(questMap);

    public bool IsSheetOpen => IsOpen(questSheet);
    public bool IsQuizOpen => IsOpen(questQuiz);
    public bool IsProgressOpen => IsOpen(questProgress);

    void SetUpQuest()
    {
        questSheet = Find<VisualElement>("quest-sheet");
        sheet = Find<VisualElement>("sheet");
        sheetSeal = Find<VisualElement>("sheet-seal");
        sheetKicker = Find<Label>("sheet-kicker");
        sheetTitle = Find<Label>("sheet-title");
        sheetBody = Find<Label>("sheet-body");
        sheetPage = Find<Label>("sheet-page");
        sheetScroll = Find<ScrollView>("sheet-scroll");
        sheetPrimary = Find<Button>("btn-sheet-primary");
        sheetVoice = Find<Button>("btn-sheet-voice");
        Wire(sheetPrimary, () => SheetPrimaryClicked?.Invoke());
        Wire(sheetVoice, () => VoiceClicked?.Invoke());

        questQuiz = Find<VisualElement>("quest-quiz");
        quizSheet = questQuiz?.Q<VisualElement>(className: "sheet");
        quizCounter = Find<Label>("quiz-counter");
        quizQuestion = Find<Label>("quiz-question");
        quizFeedback = Find<Label>("quiz-feedback");
        quizNext = Find<Button>("btn-quiz-next");
        quizVoice = Find<Button>("btn-quiz-voice");
        for (int i = 0; i < QuizAnswerSlots; i++)
        {
            int index = i;
            quizAnswers[i] = Find<Button>("quiz-answer-" + i);
            quizAnswerTexts[i] = Find<Label>("quiz-answer-text-" + i);
            Wire(quizAnswers[i], () => OnAnswerClicked(index));
        }
        Wire(quizNext, () => QuizNextClicked?.Invoke());
        Wire(quizVoice, () => VoiceClicked?.Invoke());

        questProgress = Find<VisualElement>("quest-progress");
        progressArch = Find<ArchEmblem>("progress-arch");
        progressSealRow = Find<SealRow>("progress-seal-row");
        progressPieces = Find<Label>("progress-pieces");
        progressSeals = Find<Label>("progress-seals");
        progressScore = Find<Label>("progress-score");
        for (int i = 0; i < ProgressStopSlots; i++)
        {
            int index = i;
            stopRows[i] = Find<VisualElement>("progress-stop-" + i);
            stopLabels[i] = Find<Label>("progress-stop-label-" + i);
            stopPlaces[i] = Find<Label>("progress-stop-place-" + i);
            stopStatuses[i] = Find<Label>("progress-stop-status-" + i);
            stopRereads[i] = Find<Button>("btn-progress-reread-" + i);
            Wire(stopRereads[i], () => ProgressRereadClicked?.Invoke(index));
        }
        Wire(Find<Button>("btn-progress-back"), () => ProgressBackClicked?.Invoke());
        confirmStartOver = Find<VisualElement>("confirm-start-over");
        Wire(Find<Button>("btn-progress-start-over"), () => SetVisible(confirmStartOver, true));
        Wire(Find<Button>("btn-start-over-cancel"), () => SetVisible(confirmStartOver, false));
        Wire(Find<Button>("btn-start-over-confirm"), () =>
        {
            SetVisible(confirmStartOver, false);
            StartOverConfirmed?.Invoke();
        });

        finaleYear = Find<Label>("finale-year");
        SetUpMap();
    }

    void TearDownQuest()
    {
        quizAnswered = false;
        TearDownMap();
    }

    // Sheet
    /// <summary>
    /// Shows the story sheet over the current screen. kicker: the small line above the title; page: e.g. "1 of 2"
    /// (null hides it); primary: the main button's text; seal: show the wax seal mark (a secret).
    /// </summary>
    public void ShowSheet(string kicker, string title, string body, string page, string primary, bool seal)
    {
        bool wasOpen = IsOpen(questSheet);
        SetHint(sheetKicker, kicker);
        SetHint(sheetTitle, title);
        if (sheetBody != null) sheetBody.text = body ?? "";
        SetHint(sheetPage, page);
        if (sheetPrimary != null) sheetPrimary.text = primary ?? "";
        SetVisible(sheetSeal, seal);
        if (sheetScroll != null) sheetScroll.scrollOffset = Vector2.zero;
        Open(questSheet);
        if (!wasOpen) PlayEnter(sheet, "sheet--enter");
    }

    public void HideSheet() => Close(questSheet);

    /// <summary>The voice button on the sheet and the quiz: its text says what a tap does.</summary>
    public void SetVoice(string text)
    {
        foreach (var button in new[] { sheetVoice, quizVoice })
        {
            if (button != null) button.text = text ?? "";
        }
    }

    // Quiz
    /// <summary>
    /// Shows a question: the counter ("Question 1 of 3"), the question and its answers (in the order shown).
    /// The answers come in one after the other; the button below appears once one is chosen.
    /// </summary>
    public void ShowQuizQuestion(string counter, string question, string[] answers)
    {
        bool wasOpen = IsOpen(questQuiz);
        quizAnswered = false;
        if (quizCounter != null) quizCounter.text = counter ?? "";
        if (quizQuestion != null) quizQuestion.text = question ?? "";
        if (quizFeedback != null)
        {
            quizFeedback.text = "";
            quizFeedback.AddToClassList("quiz__feedback--hidden");
        }
        SetVisible(quizNext, false);
        for (int i = 0; i < QuizAnswerSlots; i++)
        {
            var button = quizAnswers[i];
            if (button == null) continue;
            bool used = answers != null && i < answers.Length;
            SetVisible(button, used);
            if (!used) continue;
            if (quizAnswerTexts[i] != null) quizAnswerTexts[i].text = answers[i] ?? "";
            button.RemoveFromClassList("answer--right");
            button.RemoveFromClassList("answer--wrong");
            button.RemoveFromClassList("answer--dim");
            button.AddToClassList("answer--enter");
            var current = button;
            current.schedule.Execute(() => current.RemoveFromClassList("answer--enter"))
                .StartingIn(AnswerFirstDelayMs + AnswerStepMs * i);
        }
        Open(questQuiz);
        if (!wasOpen) PlayEnter(quizSheet, "sheet--enter");
    }

    /// <summary>
    /// Shows the outcome of an answer: the right one takes the patina, a wrong choice dims, the others fade;
    /// the feedback line and the next button (its text) appear.
    /// </summary>
    public void ShowQuizAnswer(int chosen, int right, string feedback, string next)
    {
        quizAnswered = true;
        for (int i = 0; i < QuizAnswerSlots; i++)
        {
            var button = quizAnswers[i];
            if (button == null) continue;
            button.EnableInClassList("answer--right", i == right);
            button.EnableInClassList("answer--wrong", i == chosen && i != right);
            button.EnableInClassList("answer--dim", i != chosen && i != right);
        }
        if (quizFeedback != null)
        {
            quizFeedback.text = feedback ?? "";
            quizFeedback.RemoveFromClassList("quiz__feedback--hidden");
        }
        if (quizNext != null) quizNext.text = next ?? "";
        SetVisible(quizNext, true);
    }

    public void HideQuiz() => Close(questQuiz);

    void OnAnswerClicked(int index)
    {
        if (quizAnswered) return; // one answer per question
        QuizAnswerChosen?.Invoke(index);
    }

    // Progress
    /// <summary>
    /// The progress register: the arch, the seals, the counts (score null hides it) and the stops in route order
    /// (each row numbered 1 to 4).
    /// </summary>
    public void ShowProgress(Func<int, bool> isCollected, Func<int, bool> isSealFound, string pieces, string seals,
                             string score, ProgressStop[] stops)
    {
        SetArch(progressArch, isCollected);
        progressSealRow?.SetFound(isSealFound);
        if (progressPieces != null) progressPieces.text = pieces ?? "";
        if (progressSeals != null) progressSeals.text = seals ?? "";
        SetHint(progressScore, score);
        for (int i = 0; i < ProgressStopSlots; i++)
        {
            bool used = stops != null && i < stops.Length;
            SetVisible(stopRows[i], used);
            if (!used) continue;
            var stop = stops[i];
            if (stopLabels[i] != null) stopLabels[i].text = (i + 1).ToString();
            if (stopPlaces[i] != null) stopPlaces[i].text = stop.place ?? "";
            SetHint(stopStatuses[i], stop.status);
            stopRows[i]?.EnableInClassList("stop--current", stop.state == 1);
            stopRows[i]?.EnableInClassList("stop--done", stop.state == 2);
            SetVisible(stopRereads[i], stop.canReread);
        }
        SetVisible(confirmStartOver, false);
        Open(questProgress);
    }

    public void HideProgress()
    {
        SetVisible(confirmStartOver, false);
        Close(questProgress);
    }

    // Finale
    /// <summary>Shows the finale's year large over the camera view (null hides it).</summary>
    public void SetFinaleYear(string year)
    {
        if (finaleYear == null) return;
        bool show = !string.IsNullOrEmpty(year);
        if (show && finaleYear.text != year || show && !IsOpen(finaleYear))
        {
            finaleYear.text = year;
            SetVisible(finaleYear, true);
            PlayEnter(finaleYear, "finale-year--enter");
        }
        else if (!show)
        {
            SetVisible(finaleYear, false);
        }
    }

    // Overlay helpers
    static bool IsOpen(VisualElement element) => element != null && !element.ClassListContains("is-hidden");

    void Open(VisualElement overlay)
    {
        SetVisible(overlay, true);
        ShowScreen(CurrentScreen); // hides the screen under it
    }

    void Close(VisualElement overlay)
    {
        if (!IsOpen(overlay)) return;
        SetVisible(overlay, false);
        ShowScreen(CurrentScreen); // the screen under it comes back
    }

    /// Adds the "before it comes in" class and removes it a moment later, so the USS transition plays.
    static void PlayEnter(VisualElement element, string enterClass)
    {
        if (element == null) return;
        element.AddToClassList(enterClass);
        element.schedule.Execute(() => element.RemoveFromClassList(enterClass)).StartingIn(SheetEnterDelayMs);
    }
}
