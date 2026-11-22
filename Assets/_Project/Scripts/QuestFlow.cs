using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The quest's story, in both modes: the greeting, the seals and their secrets, the riddles, the knowledge
/// cards, the quiz and the finale. It shows them as overlays (AppUI.Quest), reads them aloud (Narrator), makes
/// pieces and seals appear around the user (ArchPieces), and saves every step (GameProgress).
///
/// AppController owns the AR states and calls Begin() when the "play area" is ready (Anywhere: exploring the
/// placed academy; In Iasi: the camera at the quest's current place, its content shown) and End() when it is
/// left. In Iasi, QuestFlow tells AppController where to walk next (CityTargetIndex, WalkRequested).
///
/// The order: greeting -> tutorial seal -> secret -> riddle of stop 1 -> (Anywhere: the map; In Iasi: walk
/// there) -> the piece -> knowledge -> quiz -> (all right: a seal -> secret) -> riddle of stop 2 ... -> finale
/// (1845, 1890, 1963, legacy, Sturdza's letter if all five seals) -> done.
/// </summary>
public class QuestFlow : MonoBehaviour
{
    [SerializeField] AppUI ui;
    [SerializeField] Narrator narrator;
    [SerializeField] ArchPieces pieces;
    [SerializeField] PlacementController placement;
    [Tooltip("Anywhere mode: the map where the riddle's place is picked. Without it, the place counts as found.")]
    [SerializeField] QuestMap map;

    [Tooltip("The arch by the academy: the finale's time lapse (1845, 1890, 1963, the legacy) plays on it.")]
    [SerializeField] ArchModel archModel;

    /// <summary>The hint or the action changed (AppController refreshes its panel).</summary>
    public event Action Changed;

    /// <summary>In Iasi: the user is ready to walk to the next place (show the guide).</summary>
    public event Action WalkRequested;

    /// <summary>The progress was reset with "Start over".</summary>
    public event Action StartedOver;

    public GameMode Mode { get; private set; } = GameMode.Free;
    public bool Active { get; private set; }

    enum Card { None, Greeting, SealIntro, Secret, Riddle, Knowledge, Quiz, QuizResult, Finale, Letter }

    Card card;
    int cardStop = -1;          // the stop (or the seal, for Secret) the card is about
    int page;                   // knowledge page / finale step
    bool rereading;             // knowledge opened from the progress view (no quiz after it)
    bool greetedThisVisit;
    VoiceRole speechRole;       // who reads the open card, and what
    string[] speech;
    bool speechUnread;          // the card opened while the voice was off: turning it on reads it from the start

    // Quiz
    int question;
    int rightAnswers;
    int rightSlot;              // where the right answer is on screen
    readonly List<int> order = new List<int>(); // screen slot -> answer index in the content


    static QuestContent Content => QuestBook.Content;

    void OnEnable()
    {
        if (pieces != null) pieces.ItemCollected += OnItemCollected;
        if (ui != null)
        {
            ui.SheetPrimaryClicked += OnSheetPrimary;
            ui.VoiceClicked += OnVoiceClicked;
            ui.QuizAnswerChosen += OnAnswerChosen;
            ui.QuizNextClicked += OnQuizNext;
            ui.ProgressBackClicked += CloseProgress;
            ui.ProgressRereadClicked += OnReread;
            ui.StartOverConfirmed += OnStartOver;
            ui.LanguageChanged += OnLanguageChanged;
        }
        if (narrator != null) narrator.StateChanged += RefreshVoiceButton;
        if (map != null)
        {
            map.PlaceFound += OnPlaceFound;
            map.Closed += OnMapClosed;
        }
    }

    void OnMapClosed() => Changed?.Invoke();

    void OnDisable()
    {
        if (pieces != null) pieces.ItemCollected -= OnItemCollected;
        if (ui != null)
        {
            ui.SheetPrimaryClicked -= OnSheetPrimary;
            ui.VoiceClicked -= OnVoiceClicked;
            ui.QuizAnswerChosen -= OnAnswerChosen;
            ui.QuizNextClicked -= OnQuizNext;
            ui.ProgressBackClicked -= CloseProgress;
            ui.ProgressRereadClicked -= OnReread;
            ui.StartOverConfirmed -= OnStartOver;
            ui.LanguageChanged -= OnLanguageChanged;
        }
        if (narrator != null) narrator.StateChanged -= RefreshVoiceButton;
        if (map != null)
        {
            map.PlaceFound -= OnPlaceFound;
            map.Closed -= OnMapClosed;
        }
    }

    void Start() => RefreshVoiceButton();

    // Called by AppController
    /// <summary>The play area is ready: carry on with the quest from the saved progress.</summary>
    public void Begin(GameMode mode)
    {
        if (Content == null) return;
        if (Mode != mode) greetedThisVisit = false;
        Mode = mode;
        Active = true;
        Continue(fromStory: false);
    }

    /// <summary>The play area is left: the open card closes (the progress is already saved).</summary>
    public void End()
    {
        Active = false;
        CloseCards();
        archModel?.ShowProgress(animate: false); // the finale's looks end with the play area
        map?.Close();
        Changed?.Invoke();
    }

    /// <summary>Forget that the greeting was shown in this visit (a new visit to the mode).</summary>
    public void NewVisit() => greetedThisVisit = false;

    /// <summary>In Iasi: the CityTargets entry the quest leads to now (the academy, or the stop's piece).</summary>
    public int CityTargetIndex(CityTargets targets)
    {
        if (targets == null || targets.targets == null) return -1;
        var phase = GameProgress.Phase(GameMode.InCity);
        int stop = GameProgress.CurrentStop(GameMode.InCity);
        bool academy = phase == QuestPhase.Welcome || phase == QuestPhase.Finale || phase == QuestPhase.Done ||
                       stop < 0;
        for (int i = 0; i < targets.targets.Length; i++)
        {
            var target = targets.targets[i];
            if (academy ? target.kind == CityTargetKind.Academy
                        : target.kind == CityTargetKind.ArchPiece && target.piece == stop)
                return i;
        }
        return -1;
    }

    /// <summary>In Iasi: what the guide screen shows (the title and the riddle or instruction).</summary>
    public void GetGuideTexts(out string title, out string riddle)
    {
        var phase = GameProgress.Phase(GameMode.InCity);
        int stop = GameProgress.CurrentStop(GameMode.InCity);
        if (phase == QuestPhase.Welcome || stop < 0 || Content == null)
        {
            title = Localization.Ui("guide.academy");
            riddle = Localization.Ui(phase == QuestPhase.Welcome ? "guide.academyText" : "guide.returnText");
            return;
        }
        var data = Content.stops[stop];
        title = data.pieceName.Text;
        riddle = data.riddle.Text;
    }

    /// <summary>
    /// The quest's line for the panel when no piece or seal is out (null when a card is open or an item is out),
    /// and its button (null for none).
    /// </summary>
    public void GetObjective(out string hint, out string action)
    {
        hint = null;
        action = null;
        if (!Active || card != Card.None || ui.IsProgressOpen || Content == null) return;
        if (pieces.HasPiece || pieces.IsSpawning) return; // the item's own hint (AppController)
        if (map != null && map.IsOpen) return;

        if (GameProgress.PendingSeal(Mode) >= 0)
        {
            hint = Localization.Ui("quest.findSeal");
            return;
        }
        switch (GameProgress.Phase(Mode))
        {
            case QuestPhase.Welcome:
                action = Localization.Ui("quest.actionGreeting");
                break;
            case QuestPhase.Riddle:
                hint = Localization.Ui(Mode == GameMode.Free ? "quest.openMap" : "quest.readRiddle");
                action = Localization.Ui(Mode == GameMode.Free ? "quest.riddleMap" : "quest.actionRiddle");
                break;
            case QuestPhase.Knowledge:
                action = Localization.Ui("quest.actionRead");
                break;
            case QuestPhase.Quiz:
                action = Localization.Ui("quest.toQuiz");
                break;
            case QuestPhase.Finale:
                action = Localization.Ui("quest.actionFinale");
                break;
            case QuestPhase.Done:
                hint = Localization.Ui("quest.complete");
                break;
        }
    }

    /// <summary>The panel's quest button (see GetObjective).</summary>
    public void DoAction()
    {
        if (!Active || card != Card.None) return;
        switch (GameProgress.Phase(Mode))
        {
            case QuestPhase.Welcome: ShowGreeting(); break;
            case QuestPhase.Riddle:
                if (Mode == GameMode.Free) OpenMap(GameProgress.CurrentStop(Mode));
                else ShowRiddle(GameProgress.CurrentStop(Mode));
                break;
            case QuestPhase.Knowledge: ShowKnowledge(GameProgress.CurrentStop(Mode), 0, false); break;
            case QuestPhase.Quiz: StartQuiz(GameProgress.CurrentStop(Mode)); break;
            case QuestPhase.Finale: ShowFinale(-1); break;
        }
    }

    /// <summary>Opens the progress view (pieces, seals, score, the stops in route order).</summary>
    public void OpenProgress(GameMode mode)
    {
        if (Content == null) return;
        Mode = mode;
        narrator?.Stop();
        var stops = new ProgressStop[GameProgress.PieceCount];
        int current = GameProgress.CurrentStop(mode);
        for (int i = 0; i < stops.Length; i++)
        {
            var data = Content.stops[i];
            bool found = GameProgress.IsCollected(mode, i);
            bool finished = GameProgress.StopFinished(mode, i);
            string status;
            if (!found) status = i == current ? Localization.Ui("progress.current") : "";
            else if (GameProgress.QuizPerfect(mode, i)) status = Localization.Ui("progress.quizPerfect");
            else if (GameProgress.QuizDone(mode, i)) status = Localization.Ui("progress.quizDone");
            else status = data.chapter.Text;
            int points = GameProgress.Score(mode, i);
            if (mode == GameMode.Free && points >= 0)
                status = (string.IsNullOrEmpty(status) ? "" : status + "\n") +
                         Localization.Ui("progress.points", points);
            stops[i] = new ProgressStop
            {
                label = Localization.Ui("progress.stop", i + 1),
                place = found ? data.place.Text : Localization.Ui("progress.hidden"),
                status = status,
                state = finished ? 2 : i == current ? 1 : 0,
                canReread = found && GameProgress.KnowledgeRead(mode, i),
            };
        }
        string score = mode == GameMode.Free
            ? Localization.Ui("progress.score", GameProgress.TotalScore(mode),
                              GameProgress.MaxStopScore * GameProgress.PieceCount)
            : null;
        ui.ShowProgress(i => GameProgress.IsCollected(mode, i), i => GameProgress.SealFound(mode, i),
            Localization.Ui("explore.count", GameProgress.CollectedCount(mode), GameProgress.PieceCount),
            Localization.Ui("progress.seals", GameProgress.SealsFoundCount(mode), GameProgress.SealCount),
            score, stops);
        Changed?.Invoke();
    }

    // The quest's next step
    /// <summary>
    /// Shows what comes next. fromStory: right after a card was closed (a new riddle opens by itself); else on
    /// returning to the play area (a riddle or a page left halfway is offered with a button instead).
    /// </summary>
    void Continue(bool fromStory)
    {
        if (!Active || card != Card.None || Content == null) return;
        int seal = GameProgress.PendingSeal(Mode);
        if (seal >= 0)
        {
            if (!(pieces.OutKind == ItemKind.Seal && pieces.OutIndex == seal)) pieces.Spawn(ItemKind.Seal, seal);
            Changed?.Invoke();
            return;
        }
        int stop = GameProgress.CurrentStop(Mode);
        switch (GameProgress.Phase(Mode))
        {
            case QuestPhase.Welcome:
                if (!greetedThisVisit) ShowGreeting();
                break;
            case QuestPhase.Riddle:
                // Anywhere: once the place is found on the map (it has a score), its piece waits around the user.
                if (Mode == GameMode.Free && GameProgress.Score(Mode, stop) >= 0)
                {
                    if (!(pieces.OutKind == ItemKind.Piece && pieces.OutIndex == stop))
                        pieces.Spawn(ItemKind.Piece, stop);
                }
                else if (fromStory)
                {
                    ShowRiddle(stop);
                }
                break;
            case QuestPhase.Knowledge:
                if (fromStory) ShowKnowledge(stop, 0, false);
                break;
            case QuestPhase.Quiz:
                if (fromStory) StartQuiz(stop);
                break;
            case QuestPhase.Finale:
                // In Iasi the finale plays at the academy (the guide leads there); Anywhere at the placed academy.
                ShowFinale(-1);
                break;
        }
        Changed?.Invoke();
    }

    void OnItemCollected(ItemKind kind, int index)
    {
        if (!Active) return;
        if (kind == ItemKind.Seal) ShowSecret(index);
        else ShowKnowledge(index, 0, false);
    }

    // Cards
    void ShowGreeting()
    {
        greetedThisVisit = true;
        var greeting = Content.greeting;
        Open(Card.Greeting, -1, greeting.speaker.Text, greeting.title.Text, greeting.text.Text, null,
             Localization.Ui("common.continue"), seal: false);
    }

    /// After the greeting: what the wax seals are; the first seal appears when this card is closed.
    void ShowSealIntro()
    {
        Open(Card.SealIntro, -1, Content.greeting.speaker.Text, Localization.Ui("quest.sealIntroTitle"),
             Content.tutorialSeal.intro.Text, null, Localization.Ui("quiz.findSeal"), seal: true);
    }

    void ShowSecret(int seal)
    {
        var data = seal == 0 ? Content.tutorialSeal : Content.stops[seal - 1].secret;
        Open(Card.Secret, seal, Localization.Ui("quest.secretKicker"), data.title.Text, data.text.Text, null,
             Localization.Ui("common.continue"), seal: true);
    }

    void ShowRiddle(int stop)
    {
        var data = Content.stops[stop];
        string primary = Localization.Ui(Mode == GameMode.Free ? "quest.riddleMap" : "quest.riddleWalk");
        Open(Card.Riddle, stop, Localization.Ui("quest.riddleKicker"), data.pieceName.Text, data.riddle.Text, null,
             primary, seal: false);
    }

    void ShowKnowledge(int stop, int pageIndex, bool reread)
    {
        var data = Content.stops[stop];
        rereading = reread;
        page = Mathf.Clamp(pageIndex, 0, data.knowledge.Length - 1);
        bool last = page == data.knowledge.Length - 1;
        string primary = !last ? Localization.Ui("common.next")
            : reread ? Localization.Ui("common.back")
            : Localization.Ui("quest.toQuiz");
        string kicker = Localization.Ui("quest.pieceFound", data.pieceName.Text, stop + 1);
        Open(Card.Knowledge, stop, kicker, data.place.Text, data.knowledge[page].Text,
             Localization.Ui("quest.page", page + 1, data.knowledge.Length), primary, seal: false);
    }

    void ShowFinale(int step)
    {
        var finale = Content.finale;
        page = step;
        string kicker = null, title = null, body;
        string primary = Localization.Ui("common.next");
        bool seal = false;
        var cardKind = Card.Finale;
        if (step < 0)
        {
            title = Localization.Ui("finale.title");
            body = finale.intro.Text;
            ui.SetFinaleYear(null);
        }
        else if (step < finale.steps.Length)
        {
            body = finale.steps[step].text.Text;
            ui.SetFinaleYear(finale.steps[step].year);
            PlayFinaleStep(step);
        }
        else if (step == finale.steps.Length)
        {
            ui.SetFinaleYear(null);
            PlayFinaleStep(step);
            title = Localization.Ui("finale.legacyTitle");
            body = finale.legacy.Text;
            if (Mode == GameMode.Free)
                kicker = Localization.Ui("finale.score", GameProgress.TotalScore(Mode),
                                         GameProgress.MaxStopScore * GameProgress.PieceCount);
            bool letter = GameProgress.SealsFoundCount(Mode) == GameProgress.SealCount;
            if (!letter) primary = Localization.Ui("finale.finish");
        }
        else
        {
            cardKind = Card.Letter;
            kicker = Localization.Ui("quest.letterKicker");
            title = finale.letter.title.Text;
            body = finale.letter.text.Text;
            primary = Localization.Ui("finale.finish");
            seal = true;
        }
        Open(cardKind, -1, kicker, title, body, null, primary, seal);
    }

    void Open(Card kind, int stop, string kicker, string title, string body, string pageText, string primary,
              bool seal)
    {
        ui.HideQuiz();
        card = kind;
        cardStop = stop;
        ui.ShowSheet(kicker, title, body, pageText, primary, seal);
        RefreshVoiceButton(); // the UI may have been rebuilt since (e.g. a style re-import in the Editor)
        // The title is read once: not for the greeting, nor again on the knowledge pages after the first.
        bool readTitle = !string.IsNullOrEmpty(title) && kind != Card.Greeting &&
                         !(kind == Card.Knowledge && page > 0);
        Speak(SpeakerOf(kind), readTitle ? title : null, body);
        Changed?.Invoke();
    }

    /// Sturdza speaks his greeting, the seals card and his letter; the narrator everything else.
    static VoiceRole SpeakerOf(Card kind) =>
        kind == Card.Greeting || kind == Card.SealIntro || kind == Card.Letter
            ? VoiceRole.Sturdza
            : VoiceRole.Narrator;

    /// <summary>
    /// Every line the quest can speak in this language, as the cards speak them (each title and text is its own
    /// line). The narration recorder records these; keep it in step with what Open, the quiz and the finale say.
    /// </summary>
    public static void SpokenLines(QuestContent content, Language language, List<NarrationLine> lines)
    {
        void Add(VoiceRole role, string text)
        {
            if (!string.IsNullOrWhiteSpace(text)) lines.Add(new NarrationLine(role, text));
        }

        void AddText(VoiceRole role, LocalizedText text) => Add(role, text?.In(language));

        string Ui(string key, params object[] args) => Localization.UiIn(language, key, args);

        var sturdza = VoiceRole.Sturdza;
        var narrator = VoiceRole.Narrator;
        // The greeting (its title isn't read), what the seals are, the tutorial seal's secret.
        AddText(sturdza, content.greeting.text);
        Add(sturdza, Ui("quest.sealIntroTitle"));
        AddText(sturdza, content.tutorialSeal.intro);
        AddText(narrator, content.tutorialSeal.title);
        AddText(narrator, content.tutorialSeal.text);
        // The stops: riddle, knowledge (the place's name before the first page), quiz, secret.
        foreach (var stop in content.stops)
        {
            AddText(narrator, stop.pieceName);
            AddText(narrator, stop.riddle);
            AddText(narrator, stop.place);
            foreach (var page in stop.knowledge) AddText(narrator, page);
            foreach (var question in stop.quiz)
            {
                AddText(narrator, question.question);
                foreach (var answer in question.answers) AddText(narrator, answer);
                AddText(narrator, question.explanation);
            }
            AddText(narrator, stop.secret.title);
            AddText(narrator, stop.secret.text);
        }
        // The quiz's own lines: right / wrong, the result ("2 of 3 right") for every possible count.
        Add(narrator, Ui("quiz.right"));
        Add(narrator, Ui("quiz.wrong"));
        var totals = new HashSet<int>();
        foreach (var stop in content.stops) totals.Add(stop.quiz.Length);
        foreach (int total in totals)
            for (int right = 0; right <= total; right++)
                Add(narrator, Ui("quiz.resultTitle", right, total));
        Add(narrator, Ui("quiz.perfect"));
        Add(narrator, Ui("quiz.notPerfect"));
        // The finale; Sturdza reads his letter.
        Add(narrator, Ui("finale.title"));
        AddText(narrator, content.finale.intro);
        foreach (var step in content.finale.steps) AddText(narrator, step.text);
        Add(narrator, Ui("finale.legacyTitle"));
        AddText(narrator, content.finale.legacy);
        AddText(sturdza, content.finale.letter.title);
        AddText(sturdza, content.finale.letter.text);
    }

    void OnSheetPrimary()
    {
        int stop = cardStop;
        switch (card)
        {
            case Card.Greeting:
                GameProgress.MarkWelcomeDone(Mode);
                ShowSealIntro(); // what seals are, before the first one appears
                break;

            case Card.SealIntro:
                CloseCards();
                Continue(fromStory: true); // the tutorial seal appears
                break;

            case Card.Secret:
                CloseCards();
                GameProgress.MarkSealFound(Mode, stop);
                Continue(fromStory: true);
                break;

            case Card.Riddle:
                CloseCards();
                if (Mode == GameMode.Free) OpenMap(stop);
                else WalkRequested?.Invoke();
                Changed?.Invoke();
                break;

            case Card.Knowledge:
                var data = Content.stops[stop];
                if (page < data.knowledge.Length - 1)
                {
                    ShowKnowledge(stop, page + 1, rereading);
                    break;
                }
                CloseCards();
                if (rereading)
                {
                    OpenProgress(Mode);
                    break;
                }
                GameProgress.MarkKnowledgeRead(Mode, stop);
                StartQuiz(stop);
                break;

            case Card.QuizResult:
                CloseCards();
                GameProgress.MarkQuizDone(Mode, stop, rightAnswers == Content.stops[stop].quiz.Length);
                Continue(fromStory: true); // a seal (all right), else the next riddle or the finale
                break;

            case Card.Finale:
                if (page < Content.finale.steps.Length + 1 &&
                    !(page == Content.finale.steps.Length &&
                      GameProgress.SealsFoundCount(Mode) != GameProgress.SealCount))
                {
                    ShowFinale(page + 1);
                    break;
                }
                FinishFinale();
                break;

            case Card.Letter:
                FinishFinale();
                break;
        }
    }

    void FinishFinale()
    {
        CloseCards();
        ui.SetFinaleYear(null);
        GameProgress.MarkFinaleDone(Mode);
        archModel?.ShowProgress(animate: true); // In Iasi the arch now stays whole
        Changed?.Invoke();
    }

    void CloseCards()
    {
        card = Card.None;
        cardStop = -1;
        speech = null;
        narrator?.Stop();
        ui.HideSheet();
        ui.HideQuiz();
        ui.SetFinaleYear(null);
    }

    // Quiz
    void StartQuiz(int stop)
    {
        question = 0;
        rightAnswers = 0;
        cardStop = stop;
        ShowQuestion();
    }

    void ShowQuestion()
    {
        ui.HideSheet();
        card = Card.Quiz;
        var stopData = Content.stops[cardStop];
        var q = stopData.quiz[question];
        // Shuffle the answers; the content lists the right one first.
        order.Clear();
        for (int i = 0; i < q.answers.Length; i++) order.Add(i);
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        rightSlot = order.IndexOf(0);
        var texts = new string[order.Count];
        for (int i = 0; i < texts.Length; i++) texts[i] = q.answers[order[i]].Text;
        ui.ShowQuizQuestion(Localization.Ui("quiz.counter", question + 1, stopData.quiz.Length), q.question.Text,
                            texts);
        RefreshVoiceButton();
        var lines = new string[texts.Length + 1];
        lines[0] = q.question.Text;
        Array.Copy(texts, 0, lines, 1, texts.Length);
        Speak(VoiceRole.Narrator, lines); // the question, then the answers in the order shown
        Changed?.Invoke();
    }

    void OnAnswerChosen(int slot)
    {
        if (card != Card.Quiz) return;
        var stopData = Content.stops[cardStop];
        var q = stopData.quiz[question];
        bool right = slot == rightSlot;
        if (right) rightAnswers++;
        string verdict = Localization.Ui(right ? "quiz.right" : "quiz.wrong");
        string feedback = verdict + " " + q.explanation.Text;
        bool last = question == stopData.quiz.Length - 1;
        ui.ShowQuizAnswer(slot, rightSlot, feedback,
            Localization.Ui(last ? "quiz.seeResult" : "quiz.nextQuestion"));
        Speak(VoiceRole.Narrator, verdict, q.explanation.Text);
    }

    void OnQuizNext()
    {
        if (card != Card.Quiz) return;
        var stopData = Content.stops[cardStop];
        if (question < stopData.quiz.Length - 1)
        {
            question++;
            ShowQuestion();
            return;
        }
        ui.HideQuiz();
        int total = stopData.quiz.Length;
        bool perfect = rightAnswers == total;
        card = Card.QuizResult;
        string body = Localization.Ui(perfect ? "quiz.perfect" : "quiz.notPerfect");
        string title = Localization.Ui("quiz.resultTitle", rightAnswers, total);
        ui.ShowSheet(stopData.chapter.Text, title, body, null,
            Localization.Ui(perfect ? "quiz.findSeal" : "common.continue"), seal: false);
        Speak(VoiceRole.Narrator, title, body);
        Changed?.Invoke();
    }

    // Map (Anywhere)
    void OpenMap(int stop)
    {
        if (map == null)
        {
            OnPlaceFound(stop, 0); // no map in the scene: the place counts as found
            return;
        }
        narrator?.Stop();
        map.Open(stop, Content.stops[stop]);
        Changed?.Invoke();
    }

    void OnPlaceFound(int stop, int misses)
    {
        if (!Active || Mode != GameMode.Free) return;
        GameProgress.SetScore(Mode, stop, GameProgress.ScoreAfterMisses(misses));
        Debug.Log($"[QuestFlow] Stop {stop} found on the map after {misses} misses: " +
                  $"{GameProgress.Score(Mode, stop)} points.");
        Continue(fromStory: false); // its piece appears around the user
    }

    // Progress view
    void CloseProgress()
    {
        ui.HideProgress();
        Changed?.Invoke();
    }

    void OnReread(int stop)
    {
        ui.HideProgress();
        if (card != Card.None) CloseCards();
        ShowKnowledge(stop, 0, true);
    }

    void OnStartOver()
    {
        CloseCards();
        ui.HideProgress();
        pieces.Remove();
        GameProgress.ResetAll(Mode);
        archModel?.ShowProgress(animate: true); // the pieces turn back into ghosts
        greetedThisVisit = false;
        Debug.Log($"[QuestFlow] {Mode} quest started over.");
        StartedOver?.Invoke();
        if (Active) Continue(fromStory: false);
        Changed?.Invoke();
    }

    // Voice
    void Speak(VoiceRole role, params string[] lines)
    {
        speechRole = role;
        speech = lines;
        speechUnread = narrator != null && !narrator.On;
        narrator?.Speak(role, lines);
    }

    void OnVoiceClicked()
    {
        if (narrator == null) return;
        if (narrator.On && narrator.State == Narrator.VoiceState.MissingData)
        {
            narrator.OpenVoiceInstall();
            return;
        }
        // Off pauses the voice where it is; on carries on from there. A card opened while the voice was off is
        // read from its start; one already read to the end isn't read again.
        narrator.On = !narrator.On;
        if (narrator.On && !narrator.Resume() && speechUnread && speech != null)
        {
            speechUnread = false;
            narrator.Speak(speechRole, speech);
        }
        RefreshVoiceButton();
    }

    void RefreshVoiceButton()
    {
        if (ui == null || narrator == null) return;
        // The button names what a tap does (turn the voice off / on, or download it), not the current state.
        bool missing = narrator.On && narrator.State == Narrator.VoiceState.MissingData;
        string text = missing ? Localization.Ui("voice.download")
            : Localization.Ui(narrator.On ? "voice.turnOff" : "voice.turnOn");
        ui.SetVoice(text);
    }

    void OnLanguageChanged()
    {
        RefreshVoiceButton();
        Changed?.Invoke();
    }

    // Finale visuals
    /// step: 0 = 1845 (the arch rises), 1 = 1890 (it crumbles), 2 = 1963 (the academy dissolves), 3 = the legacy
    /// (the academy and the arch come back, as a memory). ArchModel animates each; nothing pops.
    void PlayFinaleStep(int step)
    {
        if (archModel == null) return;
        switch (step)
        {
            case 0: archModel.FinaleArchStands(); break;
            case 1: archModel.FinaleArchFalls(); break;
            case 2: archModel.FinaleAcademyGone(); break;
            default: archModel.FinaleMemory(); break;
        }
    }
}
