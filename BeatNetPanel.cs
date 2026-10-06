using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Arcade.UI;
using Arcade.UI.SongSelect;
using Newtonsoft.Json.Linq;
using Rewired;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BEATNET;

public sealed class BeatNetPanel : MonoBehaviour
{
    private const int PageSize = 30;
    private const float ListWidth = 842f;
    private const float DetailLeft = 914f;
    private const float DetailSize = 686f;
    private const float TextLeft = 942f;
    private const float TextWidth = 630f;
    private readonly BeatNetFrames pageFrames = new();
    private readonly BeatNetFrames detailFrames = new();
    private readonly List<Button> rows = new();
    private readonly List<Selectable> controls = new();
    private readonly List<TextMeshProUGUI> rowLabels = new();
    private readonly List<TextMeshProUGUI> rowDetails = new();
    private readonly List<TextMeshProUGUI> rowStates = new();
    private readonly List<TextMeshProUGUI> difficultyNames = new();
    private readonly List<TextMeshProUGUI> difficultyLevels = new();
    private readonly List<TextMeshProUGUI> levelLabels = new();
    private readonly List<Image> rowMarkers = new();
    private BeatNetRhythm rhythm = null!;
    private float coverAfter;
    private BeatNetCovers covers = null!;
    private RawImage detailCover = null!;
    private BeatNetFade detailCoverFade = null!;
    private int downloadVersion;
    private string downloadStamp = string.Empty;
    private string scorePath = string.Empty;
    private readonly List<EventSystem> eventSystems = new();
    private readonly List<Button> difficultyChoices = new();
    private readonly List<BeatNetFade> rowFades = new();
    private readonly Dictionary<TextMeshProUGUI, BeatNetFade> textFades = new();
    private BeatNetUi ui = null!;
    private Task<BeatNetClient> clientReady = null!;
    private BeatNetKeyboard keyboard = null!;
    private BeatNetAccountPanel accountPanel = null!;
    private BeatNetRatings ratings = null!;
    private BeatNetFilters filters = null!;
    private string ratingError = string.Empty;
    private BeatNetExploreScores exploreScores = null!;
    private string exploreScoreError = string.Empty;
    private BeatNetInput pointerInput = null!;
    private BeatNetInputModule pointerModule = null!;
    private GameObject? previousSelection;
    private EventSystem? previousEvents;
    private JeffBezosController? inputOwner;
    private EventSystem events = null!;
    private TMP_InputField search = null!;
    private Button previous = null!;
    private Button next = null!;
    private Button install = null!;
    private Button preview = null!;
    private readonly BeatNetPreview audioPreview = new();
    private Button exploreTab = null!;
    private Button libraryTab = null!;
    private TextMeshProUGUI leftPrompt = null!;
    private TextMeshProUGUI rightPrompt = null!;
    private Button play = null!;
    private Button uninstall = null!;
    private Button difficulty = null!;
    private Button close = null!;
    private GameObject difficultyPopup = null!;
    private RectTransform difficultyDialog = null!;
    private RectTransform difficultyList = null!;
    private Button difficultyBack = null!;
    private Image downloadFill = null!;
    private TextMeshProUGUI downloadText = null!;
    private TextMeshProUGUI supported = null!;
    private TextMeshProUGUI supportedRight = null!;
    private TextMeshProUGUI supportedHeading = null!;
    private TextMeshProUGUI pageLabel = null!;
    private TextMeshProUGUI heading = null!;
    private TextMeshProUGUI slogan = null!;
    private bool sloganAligned;
    private TextMeshProUGUI title = null!;
    private TextMeshProUGUI artist = null!;
    private TextMeshProUGUI mapper = null!;
    private TextMeshProUGUI highscore = null!;
    private TextMeshProUGUI rank = null!;
    private TextMeshProUGUI cleared = null!;
    private TextMeshProUGUI sizeLabel = null!;
    private TextMeshProUGUI updateLabel = null!;
    private TextMeshProUGUI empty = null!;
    private TextMeshProUGUI countLabel = null!;
    private ScrollRect list = null!;
    private RectTransform listArea = null!;
    private RectTransform window = null!;
    private RectTransform content = null!;
    private BeatNetTabs tabs = null!;
    private CanvasGroup fade = null!;
    private BeatNetMotion motion = null!;
    private BeatNetMotion difficultyMotion = null!;
    private BeatNetPerspective? backgroundPerspective;
    private BeatNetPerspective windowPerspective = null!;
    private BeatNetPerspective difficultyPerspective = null!;
    private BeatNetLoading listLoading = null!;
    private BeatNetLoading detailsLoading = null!;
    private BeatNetLoading sizeLoading = null!;
    private BeatNetLoading supportedLoading = null!;
    private bool loadingPage;
    private CanvasScaler scaler = null!;
    private TextMeshProUGUI status = null!;
    private BeatNetClient? client;
    private BeatmapInstaller installer = null!;
    private CatalogPage page = new();
    private BeatmapEntry? selected;
    private List<BeatmapEntry> libraryEntries = new();
    private Dictionary<string, BeatmapEntry> latest = new();
    private bool library;
    private bool removing;
    private string playDifficulty = string.Empty;
    private string focusId = string.Empty;
    private int revisionVersion = -1;
    private int metadataVersion = -1;
    private CancellationTokenSource? cancellation;
    private Task? pending;
    private Action? finish;
    private string query = string.Empty;
    private volatile string progress = string.Empty;
    private bool installing;
    private bool reloadNeeded;
    private int closeFrame;
    private float nextMove;
    private int lastDirection;
    private int selectedIndex = -1;
    private int shownFrame;
    private bool controller;
    private bool closing;
    private bool prepared;
    private bool openRequested;
    private Canvas canvas = null!;
    private bool visible;
    private bool drawingPage;
    private bool drawingDetails;
    private string laidOutTitle = string.Empty;
    private float laidOutHeight;
    private string laidOutScore = string.Empty;
    private string laidOutRank = string.Empty;
    private string laidOutClear = string.Empty;
    private Task<CatalogPage>? firstPage;
    private readonly CancellationTokenSource firstCancellation = new();
    private bool cursorVisible;
    private CursorLockMode cursorLock;
    private Vector3 mousePosition;
    private int screenWidth;
    private int screenHeight;

    internal bool IsOpen => prepared && visible;
    internal bool KeepCursor { get; private set; }
    internal bool IsTyping => IsOpen && (search.isFocused || keyboard.IsOpen || accountPanel.IsOpen);
    internal static bool BlocksGameInput { get; private set; }

    internal static BeatNetPanel Create(Transform parent, TextMeshProUGUI label, Graphic background, Button back, TextMeshProUGUI number)
    {
        var root = new GameObject("BEATNET.Popup", typeof(RectTransform));
        root.SetActive(false);
        root.layer = parent.gameObject.layer;
        SceneManager.MoveGameObjectToScene(root, parent.gameObject.scene);
        var canvas = root.AddComponent<Canvas>();
        var nativeCanvas = parent.GetComponentInParent<Canvas>();
        canvas.worldCamera = nativeCanvas.worldCamera ?? Camera.main;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.planeDistance = nativeCanvas.planeDistance;
        canvas.additionalShaderChannels = nativeCanvas.additionalShaderChannels;
        canvas.sortingOrder = short.MaxValue;
        root.AddComponent<GraphicRaycaster>();
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<Image>();
        var panel = root.AddComponent<BeatNetPanel>();
        panel.library = ArcadeSelection.Library;
        panel.canvas = canvas;
        panel.scaler = scaler;
        panel.events = root.AddComponent<EventSystem>();
        panel.events.sendNavigationEvents = false;
        panel.pointerModule = root.AddComponent<BeatNetInputModule>();
        panel.pointerInput = root.AddComponent<BeatNetInput>();
        panel.pointerModule.inputOverride = panel.pointerInput;
        panel.clientReady = Task.Run(() => new BeatNetClient("http://92.5.175.72"));
        var firstToken = panel.firstCancellation.Token;
        panel.firstPage = Task.Run(async () =>
        {
            var source = await panel.clientReady.ConfigureAwait(false);
            return await source.List(string.Empty, 0, PageSize, firstToken).ConfigureAwait(false);
        });
        _ = panel.firstPage.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        var perspective = parent.GetComponentInParent<CanvasMousePerspective>();
        if (perspective != null)
        {
            panel.backgroundPerspective = new BeatNetPerspective(perspective);
        }
        panel.Build(label, background, back, number);
        panel.installer = new BeatmapInstaller(Application.persistentDataPath);
        panel.covers = new BeatNetCovers(Application.persistentDataPath);
        return panel;
    }

    private void Build(TextMeshProUGUI label, Graphic background, Button back, TextMeshProUGUI number)
    {
        ui = new BeatNetUi(label, background);
        ui.Tint(GetComponent<Image>(), BeatNetColor.Backdrop);
        window = ui.Rect(transform, "Window", 0f, 0f, 1640f, 940f);
        window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
        window.pivot = new Vector2(0.5f, 0.5f);
        window.anchoredPosition = Vector2.zero;
        windowPerspective = new BeatNetPerspective(window.gameObject.AddComponent<CanvasMousePerspective>());
        ui.Tint(window.gameObject.AddComponent<Image>(), BeatNetColor.Background);
        fade = gameObject.AddComponent<CanvasGroup>();
        motion = BeatNetMotion.Create(gameObject, window, visibility: SetVisible);
        ui.Backdrop(window);
        close = ui.Back(window, back, number);
        close.onClick.AddListener(() =>
        {
            if (pointerInput.GetMouseButtonUp(0) || pointerInput.GetMouseButtonDown(0))
            {
                UseKeyboard();
            }
            Close();
        });
        heading = ui.Text(window, "BEATNET", 72f, 340f, 34f, 960f, 84f, BeatNetColor.Accent, BeatNetFont.Display);
        heading.alignment = TextAlignmentOptions.Center;
        heading.fontStyle = FontStyles.Italic;
        heading.overflowMode = TextOverflowModes.Overflow;
        slogan = ui.Text(window, "search beatmaps. online.", 13f, 530f, 26f, 574f, 22f, BeatNetColor.Accent);
        slogan.alignment = TextAlignmentOptions.Right;
        slogan.characterSpacing = 4f;
        exploreTab = ui.Button(window, "Explore", 612f, 132f, 200f, 48f, true);
        ((BeatNetControl)exploreTab).Sound = BeatNetSound.None;
        exploreTab.onClick.AddListener(() => SetLibrary(false));
        libraryTab = ui.Button(window, "Library", 828f, 132f, 200f, 48f);
        ((BeatNetControl)libraryTab).Sound = BeatNetSound.None;
        libraryTab.onClick.AddListener(() => SetLibrary(true));
        leftPrompt = ui.Text(window, "LB", 24f, 526f, 132f, 70f, 48f, BeatNetColor.Text, BeatNetFont.Button);
        leftPrompt.alignment = TextAlignmentOptions.Center;
        rightPrompt = ui.Text(window, "RB", 24f, 1044f, 132f, 70f, 48f, BeatNetColor.Text, BeatNetFont.Button);
        rightPrompt.alignment = TextAlignmentOptions.Center;
        leftPrompt.gameObject.SetActive(controller);
        rightPrompt.gameObject.SetActive(controller);
        ui.Fill(window, "Divider", BeatNetColor.Line, 40f, 188f, 1560f, 1f);
        content = ui.Rect(window, "Content", 0f, 0f, 1640f, 940f);
        var clip = ui.Rect(window, "Content mask", 0f, 196f, 1640f, 744f);
        clip.gameObject.AddComponent<RectMask2D>();
        content.SetParent(clip, false);
        content.anchoredPosition = new Vector2(0f, 196f);
        ui.Fill(content, "Content background", BeatNetColor.Background, 0f, 196f, 1640f, 744f);
        tabs = BeatNetTabs.Create(content);
        search = ui.Search(content, 40f, 214f, 674f, 58f);
        search.onValueChanged.AddListener(_ =>
        {
            if (search.isFocused)
            {
                BeatNetSounds.Play(BeatNetSound.Down);
            }
        });
        search.onSubmit.AddListener(_ =>
        {
            search.DeactivateInputField();
            events.SetSelectedGameObject(controls[1].gameObject);
            Search();
        });
        controls.Add(search);
        var find = ui.Button(content, "Search", 730f, 214f, 152f, 58f, true);
        ((BeatNetControl)find).Sound = BeatNetSound.None;
        find.onClick.AddListener(Search);
        controls.Add(find);
        controls.Add(exploreTab);
        controls.Add(libraryTab);
        countLabel = ui.Text(content, "", 17f, 668f, 286f, 214f, 30f, BeatNetColor.Muted);
        countLabel.alignment = TextAlignmentOptions.Right;
        list = ui.List(content, 40f, 326f, ListWidth, 508f);
        listArea = list.viewport;
        empty = ui.Text(listArea, "Loading beatmaps", 27f, 28f, 120f, ListWidth - 56f, 180f, BeatNetColor.Muted);
        empty.alignment = TextAlignmentOptions.Center;
        empty.textWrappingMode = TextWrappingModes.Normal;
        for (var index = 0; index < PageSize; index++)
        {
            var row = ui.Button(list.content, "", 0f, index * 86f, ListWidth, 78f);
            ((BeatNetControl)row).Sound = BeatNetSound.None;
            var text = row.GetComponentInChildren<TextMeshProUGUI>(true);
            text.alignment = TextAlignmentOptions.Left;
            text.fontSize = 25f;
            ui.Font(text, BeatNetFont.Button);
            text.rectTransform.anchoredPosition = new Vector2(22f, -4f);
            text.rectTransform.sizeDelta = new Vector2(ListWidth - 48f, 39f);
            rowDetails.Add(ui.Text(row.transform, "", 18f, 22f, 43f, 510f, 27f, BeatNetColor.Muted));
            var state = ui.Text(row.transform, "", 15f, 530f, 43f, ListWidth - 554f, 27f, BeatNetColor.AccentText);
            state.alignment = TextAlignmentOptions.Right;
            rowStates.Add(state);
            rowMarkers.Add(ui.Fill(row.transform, "Selected", BeatNetColor.Accent, 0f, 0f, 4f, 78f));
            rowFades.Add(BeatNetFade.Create(row.gameObject));
            var rowIndex = index;
            row.onClick.AddListener(() => SelectRow(rowIndex, true));
            rows.Add(row);
            rowLabels.Add(text);
            controls.Add(row);
            row.gameObject.SetActive(false);
        }
        previous = ui.Button(content, "< Previous", 40f, 852f, 164f, 48f, true);
        ((BeatNetControl)previous).Sound = BeatNetSound.None;
        previous.onClick.AddListener(() => ChangePage(-1));
        controls.Add(previous);
        pageLabel = ui.Text(content, "", 18f, 382f, 852f, 158f, 48f, BeatNetColor.Muted);
        pageLabel.alignment = TextAlignmentOptions.Center;
        next = ui.Button(content, "Next >", 718f, 852f, 164f, 48f, true);
        ((BeatNetControl)next).Sound = BeatNetSound.None;
        next.onClick.AddListener(() => ChangePage(1));
        controls.Add(next);
        ui.Fill(content, "Details", BeatNetColor.Surface, DetailLeft, 214f, DetailSize, DetailSize);
        detailCover = CreateCover(content, DetailLeft, 214f, DetailSize, DetailSize);
        detailCoverFade = BeatNetFade.Create(detailCover.gameObject);
        ui.Fill(content, "Accent", BeatNetColor.Accent, DetailLeft, 214f, DetailSize, 3f);
        title = ui.Text(content, "Choose a beatmap", 52f, TextLeft, 242f, TextWidth, 96f);
        title.alignment = TextAlignmentOptions.TopLeft;
        title.enableAutoSizing = true;
        title.fontSizeMin = 28f;
        title.fontSizeMax = 52f;
        title.textWrappingMode = TextWrappingModes.Normal;
        ui.Font(title, BeatNetFont.Heading);
        artist = ui.Text(content, "", 30f, TextLeft, 344f, TextWidth, 48f, BeatNetColor.Text, BeatNetFont.Heading);
        artist.alignment = TextAlignmentOptions.TopLeft;
        mapper = ui.Text(content, "", 22f, TextLeft, 392f, TextWidth, 36f, BeatNetColor.Muted);
        mapper.alignment = TextAlignmentOptions.TopLeft;
        highscore = ui.Text(content, "", 56f, TextLeft, 424f, 360f, 80f, BeatNetColor.Text, BeatNetFont.Score);
        highscore.richText = true;
        highscore.alignment = TextAlignmentOptions.BottomLeft;
        highscore.enableAutoSizing = true;
        highscore.fontSizeMin = 48f;
        highscore.fontSizeMax = 56f;
        highscore.rectTransform.localScale = new Vector3(1f, 0.9f, 1f);
        rank = ui.Text(content, "", 46f, 1270f, 424f, 84f, 80f, BeatNetColor.Text, BeatNetFont.Rank);
        rank.richText = true;
        rank.alignment = TextAlignmentOptions.BottomLeft;
        rank.rectTransform.localScale = new Vector3(1f, 0.9f, 1f);
        cleared = ui.Text(content, "", 80f * 13.64f / 48.1f, 1140f, 424f, 192f, 36f, BeatNetColor.Text);
        cleared.richText = true;
        cleared.fontStyle = FontStyles.LowerCase;
        cleared.characterSpacing = 11f;
        cleared.alignment = TextAlignmentOptions.BottomRight;
        cleared.overflowMode = TextOverflowModes.Overflow;
        ui.Fill(content, "Divider", BeatNetColor.Line, TextLeft, 514f, TextWidth, 1f);
        supportedHeading = ui.Text(content, "DIFFICULTIES", 16f, TextLeft, 530f, TextWidth, 24f, BeatNetColor.Muted, BeatNetFont.Button);
        for (var index = 0; index < 6; index++)
        {
            var left = index < 3 ? TextLeft : TextLeft + 318f;
            var top = 554f + index % 3 * 28f;
            var level = ui.Text(content, "", 26f, left, top + 6f, 48f, 28f, BeatNetColor.Text, BeatNetFont.Level);
            level.richText = true;
            level.rectTransform.localScale = new Vector3(1f, 0.9f, 1f);
            difficultyLevels.Add(level);
            var levelLabel = ui.Text(level.transform, "", 8.7f, 27f, -5f, 21f, 12f, BeatNetColor.Text, BeatNetFont.Level);
            levelLabel.richText = true;
            levelLabels.Add(levelLabel);
            var name = ui.Text(content, "", 21f, left + 56f, top + 2f, 250f, 32f, BeatNetColor.Text, BeatNetFont.Button);
            name.richText = true;
            name.fontStyle = FontStyles.LowerCase;
            name.alignment = TextAlignmentOptions.MidlineLeft;
            difficultyNames.Add(name);
        }
        supported = difficultyNames[0];
        supportedRight = difficultyNames[3];
        sizeLabel = ui.Text(content, "", 19f, TextLeft, 646f, 260f, 30f, BeatNetColor.Muted);
        updateLabel = ui.Text(content, "", 18f, TextLeft + 278f, 646f, 352f, 30f, BeatNetColor.AccentText);
        updateLabel.alignment = TextAlignmentOptions.Right;
        difficulty = ui.Button(content, "", TextLeft, 696f, TextWidth, 48f, true);
        difficulty.GetComponentInChildren<TextMeshProUGUI>(true).richText = true;
        difficulty.GetComponentInChildren<TextMeshProUGUI>(true).fontStyle = FontStyles.LowerCase;
        ((BeatNetControl)difficulty).Sound = BeatNetSound.None;
        ((BeatNetControl)difficulty).HoverSound = BeatNetSound.Hover;
        difficulty.onClick.AddListener(OpenDifficulties);
        controls.Add(difficulty);
        install = ui.Button(content, "Download", TextLeft, 766f, TextWidth, 60f, true);
        ((BeatNetControl)install).HoverSound = BeatNetSound.Hover;
        install.onClick.AddListener(Install);
        controls.Add(install);
        downloadFill = ui.Fill(install.transform, "Progress", BeatNetColor.Accent, 0f, 0f, 0f, 60f);
        downloadFill.gameObject.AddComponent<RectMask2D>();
        downloadText = ui.Text(downloadFill.transform, "", 24f, 16f, 0f, TextWidth - 32f, 60f, BeatNetColor.OnAccent, BeatNetFont.Button);
        downloadText.alignment = TextAlignmentOptions.Center;
        downloadFill.gameObject.SetActive(false);
        play = ui.Button(content, "Play", TextLeft, 766f, TextWidth, 60f, true);
        ((BeatNetControl)play).Sound = BeatNetSound.None;
        ((BeatNetControl)play).HoverSound = BeatNetSound.Hover;
        play.onClick.AddListener(Play);
        controls.Add(play);
        uninstall = ui.Button(content, "Uninstall", TextLeft, 842f, TextWidth, 30f);
        ui.Style(uninstall, inverted: true);
        ((BeatNetControl)uninstall).HoverSound = BeatNetSound.Hover;
        uninstall.onClick.AddListener(Uninstall);
        controls.Add(uninstall);
        preview = ui.Button(content, "Preview", TextLeft, 738f, TextWidth, 48f, true);
        preview.onClick.AddListener(TogglePreview);
        controls.Add(preview);
        controls.Add(close);
        status = ui.Text(content, "", 15f, 40f, 906f, ListWidth, 24f, BeatNetColor.Muted);
        foreach (var text in new[] { title, artist, mapper, highscore, rank, cleared, sizeLabel, countLabel, pageLabel, empty }.Concat(difficultyNames).Concat(difficultyLevels))
        {
            textFades[text] = BeatNetFade.Create(text.gameObject);
        }
        BuildLoading();
        rhythm = new BeatNetRhythm(ui, window, content);
        BuildDifficultyPopup();
        ratings = new BeatNetRatings(ui, content);
        ratings.Saved = UpdateRating;
        exploreScores = new BeatNetExploreScores(async (entry, modifiers, token) =>
        {
            var accounts = Plugin.Accounts ?? throw new InvalidOperationException("BEATNET is not ready");
            var result = await accounts.Request(new JObject { ["action"] = "highscores", ["projectId"] = entry.Id,
                ["revisionId"] = entry.Revision.Id, ["modifiers"] = modifiers }, token).ConfigureAwait(false);
            return result.ToObject<GlobalScores>() ?? throw new InvalidDataException("The server returned empty global highscores");
        });
        filters = new BeatNetFilters(ui, transform, content, events, () =>
        {
            focusId = library ? selected?.Id ?? ArcadeSelection.LibrarySong : string.Empty;
            LoadPage(0);
        });
        controls.Insert(4, filters.Sort);
        controls.Insert(5, filters.Difficulty);
        controls.Add(ratings.Slider);
        keyboard = new BeatNetKeyboard(ui, transform, search, events, Search,
            () => events.SetSelectedGameObject(controls[1].gameObject));
        accountPanel = new BeatNetAccountPanel(ui, transform, window, events);
        controls.Add(accountPanel.Manage);
        RefreshControls();
    }

    private void BuildLoading()
    {
        var cards = ui.Rect(listArea, "Loading", 0f, 0f, ListWidth, 508f);
        ui.Tint(cards.gameObject.AddComponent<Image>(), BeatNetColor.Background);
        cards.GetComponent<Image>().raycastTarget = false;
        for (var index = 0; index < 6; index++)
        {
            var top = index * 86f;
            ui.Fill(cards, "Card", BeatNetColor.Card, 0f, top, ListWidth, 78f);
            ui.Fill(cards, "Title", BeatNetColor.Highlight, 22f, top + 16f, index % 2 == 0 ? 470f : 580f, 22f);
            ui.Fill(cards, "Mapper", BeatNetColor.Surface, 22f, top + 49f, 250f, 14f);
        }
        listLoading = BeatNetLoading.Create(cards);
        var details = ui.Rect(content, "Loading", TextLeft, 242f, TextWidth, 396f);
        ui.Tint(details.gameObject.AddComponent<Image>(), BeatNetColor.Surface);
        details.GetComponent<Image>().raycastTarget = false;
        ui.Fill(details, "Title", BeatNetColor.Highlight, 0f, 8f, 510f, 48f);
        ui.Fill(details, "Title", BeatNetColor.Highlight, 0f, 72f, 350f, 48f);
        ui.Fill(details, "Artist", BeatNetColor.Card, 0f, 180f, 310f, 26f);
        ui.Fill(details, "Mapper", BeatNetColor.Card, 0f, 312f, 230f, 26f);
        ui.Fill(details, "Difficulty", BeatNetColor.Card, 320f, 312f, 240f, 26f);
        ui.Fill(details, "Size", BeatNetColor.Card, 0f, 370f, 100f, 18f);
        detailsLoading = BeatNetLoading.Create(details);
        var size = ui.Rect(content, "Loading size", TextLeft, 646f, 260f, 30f);
        ui.Fill(size, "Size", BeatNetColor.Card, 0f, 6f, 100f, 18f);
        sizeLoading = BeatNetLoading.Create(size);
        var supported = ui.Rect(content, "Loading difficulties", TextLeft, 554f, TextWidth, 90f);
        for (var index = 0; index < 3; index++)
        {
            ui.Fill(supported, "Difficulty", BeatNetColor.Card, 0f, index * 28f + 4f, 140f, 22f);
        }
        supportedLoading = BeatNetLoading.Create(supported);
    }

    private void BuildDifficultyPopup()
    {
        var overlay = ui.Button(transform, "", 0f, 0f, 0f, 0f);
        ((BeatNetControl)overlay).Sound = BeatNetSound.None;
        ((BeatNetControl)overlay).HoverSound = BeatNetSound.None;
        var overlayRect = (RectTransform)overlay.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.pivot = new Vector2(0.5f, 0.5f);
        overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;
        difficultyPopup = overlay.gameObject;
        difficultyPopup.name = "Difficulties";
        overlay.transition = Selectable.Transition.None;
        ui.Tint((Image)overlay.targetGraphic, BeatNetColor.Backdrop);
        overlay.onClick.AddListener(CloseDifficulties);
        difficultyDialog = ui.Rect(overlay.transform, "Dialog", 490f, 280f, 660f, 380f);
        difficultyDialog.pivot = new Vector2(0.5f, 0.5f);
        difficultyDialog.anchorMin = difficultyDialog.anchorMax = new Vector2(0.5f, 0.5f);
        difficultyDialog.anchoredPosition = Vector2.zero;
        difficultyPerspective = new BeatNetPerspective(difficultyDialog.gameObject.AddComponent<CanvasMousePerspective>());
        var dialogImage = difficultyDialog.gameObject.AddComponent<Image>();
        ui.Tint(dialogImage, BeatNetColor.Background);
        var dialogButton = difficultyDialog.gameObject.AddComponent<Button>();
        dialogButton.targetGraphic = dialogImage;
        dialogButton.transition = Selectable.Transition.None;
        dialogButton.navigation = new Navigation { mode = Navigation.Mode.None };
        ui.Fill(difficultyDialog, "Accent", BeatNetColor.Accent, 0f, 0f, 660f, 3f);
        ui.Text(difficultyDialog, "Choose difficulty", 34f, 30f, 22f, 600f, 64f, BeatNetColor.Text, BeatNetFont.Heading);
        difficultyList = ui.Rect(difficultyDialog, "Choices", 30f, 100f, 600f, 194f);
        difficultyBack = ui.Button(difficultyDialog, "Back", 30f, 314f, 600f, 48f, true);
        ((BeatNetControl)difficultyBack).Sound = BeatNetSound.None;
        ((BeatNetControl)difficultyBack).HoverOnly = true;
        difficultyBack.onClick.AddListener(CloseDifficulties);
        difficultyMotion = BeatNetMotion.Create(difficultyPopup, difficultyDialog, new Vector2(120f, -120f));
        difficultyPopup.SetActive(false);
    }

    private void OpenDifficulties()
    {
        if (!library || installing || pending != null)
        {
            return;
        }
        var choices = PlayableSongs().OrderByDescending(item => item.Beatmap.metadata.tagData.Level)
            .ThenByDescending(item => Array.IndexOf(ArcadeSongDatabase.Instance.BeatmapIndex.Difficulties, item.BeatmapInfo.difficulty))
            .Select(item => item.BeatmapInfo.difficulty).Distinct().ToArray();
        if (choices.Length == 0)
        {
            return;
        }
        BeatNetSounds.Play(BeatNetSound.Confirm);
        foreach (var button in difficultyChoices)
        {
            button.gameObject.SetActive(false);
        }
        for (var index = 0; index < choices.Length; index++)
        {
            var value = choices[index];
            Button button;
            if (index < difficultyChoices.Count)
            {
                button = difficultyChoices[index];
            }
            else
            {
                button = ui.Button(difficultyList, "", 0f, index * 62f, 600f, 54f, true);
                button.GetComponentInChildren<TextMeshProUGUI>(true).richText = true;
                button.GetComponentInChildren<TextMeshProUGUI>(true).fontStyle = FontStyles.LowerCase;
                ((BeatNetControl)button).HoverOnly = true;
                difficultyChoices.Add(button);
            }
            button.gameObject.SetActive(true);
            var song = PlayableSongs().First(item => item.BeatmapInfo.difficulty == value);
            button.GetComponentInChildren<TextMeshProUGUI>(true).text = BeatNetScoreText.Difficulty(song.Beatmap.metadata.GetDifficulty(value));
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                playDifficulty = value;
                HideDifficulties();
                RefreshControls();
            });
            ui.Style(button, true);
        }
        var spacing = Mathf.Min(62f, 496f / choices.Length);
        var height = choices.Length * spacing - 8f;
        difficultyDialog.sizeDelta = new Vector2(660f, height + 186f);
        difficultyList.sizeDelta = new Vector2(600f, height);
        for (var index = 0; index < choices.Length; index++)
        {
            var rect = (RectTransform)difficultyChoices[index].transform;
            rect.anchoredPosition = new Vector2(0f, -index * spacing);
            rect.sizeDelta = new Vector2(600f, spacing - 8f);
            var label = difficultyChoices[index].GetComponentInChildren<TextMeshProUGUI>(true);
            label.rectTransform.sizeDelta = new Vector2(568f, spacing - 8f);
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.fontSizeMax = 22f;
        }
        difficultyBack.GetComponent<RectTransform>().anchoredPosition = new Vector2(30f, -height - 120f);
        difficultyPerspective.Set(3.4f);
        difficultyMotion.Show();
        Canvas.ForceUpdateCanvases();
        var selectedChoice = difficultyChoices[Math.Max(0, Array.IndexOf(choices, playDifficulty))];
        events.SetSelectedGameObject(controller ? selectedChoice.gameObject : null);
    }

    private void CloseDifficulties()
    {
        if (!difficultyPopup.activeSelf || difficultyMotion.IsHiding)
        {
            return;
        }
        BeatNetSounds.Play(BeatNetSound.Back);
        HideDifficulties();
    }

    private void HideDifficulties()
    {
        difficultyMotion.Hide(finished: () =>
        {
            if (IsOpen && !closing && difficulty.gameObject.activeInHierarchy && difficulty.interactable)
            {
                events.SetSelectedGameObject(controller ? difficulty.gameObject : null);
            }
        });
    }

    internal System.Collections.IEnumerator Prepare(Func<bool> canOpen)
    {
        yield return ui.PrepareShaders();
        if (this == null)
        {
            yield break;
        }
        WarmCatalog();
        yield return BeatNetWarmup.Run(gameObject, fade, () =>
        {
            SetVisible(false);
            prepared = true;
            leftPrompt.gameObject.SetActive(controller);
            rightPrompt.gameObject.SetActive(controller);
            if (openRequested)
            {
                openRequested = false;
                if (canOpen())
                {
                    Show();
                }
            }
        }, keepActive: true);
    }

    private void WarmCatalog()
    {
        if (firstPage?.Status != TaskStatus.RanToCompletion)
        {
            return;
        }
        var result = firstPage.Result;
        for (var index = 0; index < Math.Min(rows.Count, result.Items.Length); index++)
        {
            var entry = result.Items[index];
            rowLabels[index].text = entry.Artist.Length == 0 ? entry.Title : $"{entry.Title} - {entry.Artist}";
            rowDetails[index].text = entry.Creator;
        }
        if (result.Items.Length > 0)
        {
            title.text = result.Items[0].Title;
            mapper.text = result.Items[0].Creator;
            artist.text = result.Items[0].Artist;
        }
    }

    private void SetVisible(bool active)
    {
        visible = active;
        canvas.enabled = active;
        events.enabled = active;
        pointerModule.enabled = active;
        pointerInput.enabled = active;
        enabled = active;
        GetComponent<GraphicRaycaster>().enabled = active;
        if (!active)
        {
            fade.alpha = 0f;
            fade.interactable = false;
            fade.blocksRaycasts = false;
            closeFrame = Time.frameCount;
        }
    }

    internal void SetController(bool active)
    {
        if (controller == active)
        {
            return;
        }
        controller = active;
        ui.SetController(active);
        filters.SetController(active);
        if (prepared && !active && difficultyPopup.activeSelf)
        {
            events.SetSelectedGameObject(null);
        }
        leftPrompt.gameObject.SetActive(active);
        rightPrompt.gameObject.SetActive(active);
        if (IsOpen)
        {
            RefreshControls();
        }
    }

    internal void Show()
    {
        if (!prepared)
        {
            openRequested = true;
            return;
        }
        if (IsOpen || inputOwner != null || JeffBezosController.instance == null || !JeffBezosController.instance.UIInputEnabled)
        {
            return;
        }
        previousEvents = EventSystem.current;
        previousSelection = previousEvents?.currentSelectedGameObject;
        cursorVisible = Cursor.visible;
        cursorLock = Cursor.lockState;
        mousePosition = pointerInput.mousePosition;
        shownFrame = Time.frameCount;
        lastDirection = 0;
        closing = false;
        tabs.Reset();
        library = ArcadeSelection.Library;
        if (library && focusId.Length == 0) { focusId = ArcadeSelection.LibrarySong; }
        KeepCursor = false;
        backgroundPerspective?.Set(0.25f, 3f);
        windowPerspective.Set(1.4f);
        window.localRotation = Quaternion.identity;
        foreach (var system in FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
        {
            if (system != events && system.enabled)
            {
                eventSystems.Add(system);
            }
        }
        inputOwner = JeffBezosController.instance;
        BlocksGameInput = true;
        inputOwner.DisableUIInputs();
        foreach (var system in eventSystems)
        {
            system.enabled = false;
        }
        motion.Show();
        BeatNetSounds.Play(BeatNetSound.Confirm);
        ui.RefreshTheme();
        EventSystem.current = events;
        events.SetSelectedGameObject(controller ? controls[1].gameObject : null);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        try
        {
            latest.Clear();
            ReadRevisions();
            LoadPage(0);
        }
        catch (Exception error)
        {
            ShowError(error);
        }
    }

    internal bool ShowLibrary(string id)
    {
        if (closing || IsOpen && installing) { return false; }
        if (IsOpen && library && focusId == id) { return true; }
        focusId = id;
        query = string.Empty;
        search.SetTextWithoutNotify(string.Empty);
        ArcadeSelection.SaveLibrary(true);
        if (IsOpen)
        {
            keyboard.Hide();
            accountPanel.Hide();
            tabs.Reset();
            library = true;
            LoadPage(0);
        }
        else { Show(); }
        return IsOpen;
    }

    internal void Close(bool immediate = false, Action? finished = null)
    {
        openRequested = false;
        if (!IsOpen || closing && !immediate)
        {
            return;
        }
        if (!immediate && finished == null)
        {
            BeatNetSounds.Play(BeatNetSound.Back);
        }
        closing = true;
        pageFrames.Clear();
        detailFrames.Clear();
        drawingPage = drawingDetails = false;
        audioPreview.Stop();
        tabs.Reset();
        KeepCursor = !controller;
        keyboard.Hide();
        accountPanel.Hide();
        filters.Hide();
        difficultyPopup.SetActive(false);
        cancellation?.Cancel();
        search.DeactivateInputField();
        events.SetSelectedGameObject(null);
        motion.Hide(immediate, finished);
    }

    internal bool RestoreInput(bool immediate = false)
    {
        if (inputOwner == null || (!immediate && (IsOpen || Time.frameCount <= closeFrame)))
        {
            return false;
        }
        PollWork();
        if (!immediate && pending != null)
        {
            return false;
        }
        if (reloadNeeded)
        {
            reloadNeeded = false;
            CustomSongLoader.Reload();
        }
        inputOwner.EnableUIInputs();
        inputOwner = null;
        BlocksGameInput = false;
        foreach (var system in eventSystems)
        {
            if (system != null)
            {
                system.enabled = true;
            }
        }
        eventSystems.Clear();
        if (previousEvents != null)
        {
            EventSystem.current = previousEvents;
        }
        if (previousSelection != null && previousSelection.activeInHierarchy && previousEvents != null)
        {
            previousEvents.SetSelectedGameObject(previousSelection);
        }
        previousSelection = null;
        previousEvents = null;
        RestorePerspective();
        RestoreCursor();
        return true;
    }

    private void RestorePerspective()
    {
        backgroundPerspective?.Restore();
    }

    internal void RestoreCursor()
    {
        Cursor.visible = !controller || cursorVisible;
        Cursor.lockState = controller ? cursorLock : CursorLockMode.None;
        if (!controller)
        {
            foreach (var raycaster in FindObjectsByType<GraphicRaycaster>(FindObjectsSortMode.None))
            {
                raycaster.enabled = true;
            }
        }
    }

    internal void Back()
    {
        if (filters.IsOpen) { filters.Close(); return; }
        if (accountPanel.IsOpen) { accountPanel.Close(); return; }
        if (keyboard.IsOpen)
        {
            keyboard.Close();
            return;
        }
        if (difficultyPopup.activeSelf)
        {
            CloseDifficulties();
            return;
        }
        if (IsTyping)
        {
            BeatNetSounds.Play(BeatNetSound.Back);
            search.DeactivateInputField();
            events.SetSelectedGameObject(controls[1].gameObject);
            return;
        }
        Close();
    }

    private void SetLibrary(bool value)
    {
        if (installing || tabs.IsMoving || library == value)
        {
            return;
        }
        BeatNetSounds.Play(BeatNetSound.Confirm);
        search.DeactivateInputField();
        events.SetSelectedGameObject(null);
        CancelWork();
        audioPreview.Stop();
        ArcadeSelection.SaveLibrary(value);
        tabs.Switch(value, () =>
        {
            library = value;
            focusId = value ? ArcadeSelection.LibrarySong : string.Empty;
            query = search.text.Trim();
            LoadPage(0);
        });
    }

    private void Search()
    {
        if (installing)
        {
            return;
        }
        BeatNetSounds.Play(BeatNetSound.Confirm);
        query = search.text.Trim();
        LoadPage(0);
    }

    private void ChangePage(int direction)
    {
        if (drawingPage || pending != null || (direction < 0 && !previous.interactable) || (direction > 0 && !next.interactable))
        {
            return;
        }
        BeatNetSounds.Play(direction > 0 ? BeatNetSound.Up : BeatNetSound.Down);
        LoadPage(Math.Max(0, page.Offset + direction * PageSize));
    }

    private void LoadPage(int offset)
    {
        ResetCovers();
        pageFrames.Clear();
        detailFrames.Clear();
        drawingPage = drawingDetails = false;
        difficultyPopup.SetActive(false);
        filters.Hide();
        ratings.Show(null, library);
        audioPreview.Stop();
        CancelWork();
        loadingPage = true;
        listLoading.Show();
        detailsLoading.Show();
        foreach (var item in textFades.Values)
        {
            item.Clear();
        }
        selected = null;
        selectedIndex = -1;
        playDifficulty = string.Empty;
        foreach (var row in rows)
        {
            row.gameObject.SetActive(false);
        }
        empty.gameObject.SetActive(false);
        pageLabel.text = string.Empty;
        countLabel.text = string.Empty;
        title.text = string.Empty;
        mapper.text = sizeLabel.text = artist.text = updateLabel.text = string.Empty;
        supported.text = supportedRight.text = string.Empty;
        foreach (var text in difficultyNames.Concat(difficultyLevels).Concat(levelLabels))
        {
            text.text = string.Empty;
        }
        list.StopMovement();
        list.content.anchoredPosition = Vector2.zero;
        status.text = string.Empty;
        RefreshControls();
        var local = library;
        var filter = query;
        var sorting = filters.Sorting;
        var difficulties = filters.Difficulties.ToArray();
        var cachedRatings = libraryEntries.ToDictionary(item => item.Id, item => (item.Rating, item.RatingCount));
        var focused = focusId;
        var source = client;
        var cachedPage = !local && filter.Length == 0 && offset == 0 && sorting == "title" && difficulties.Length == 0 ? firstPage : null;
        if (cachedPage != null)
        {
            firstPage = null;
            if (cachedPage.IsFaulted || cachedPage.IsCanceled)
            {
                cachedPage = null;
            }
        }
        Run(async token =>
        {
            var scan = Task.Run(() => installer.Library(), token);
            source = await clientReady.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var remote = local ? null : cachedPage ?? source!.List(filter, offset, PageSize, token, sorting, difficulties);
            if (remote != null)
            {
                await Task.WhenAll(scan, remote).ConfigureAwait(false);
            }
            var entries = await scan.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            CatalogPage result;
            if (local)
            {
                foreach (var entry in entries)
                {
                    if (cachedRatings.TryGetValue(entry.Id, out var rating)) { entry.Rating = rating.Rating; entry.RatingCount = rating.RatingCount; }
                }
                using var ratingTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                ratingTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                try
                {
                    var summaries = await source!.Ratings(entries.Where(item => BeatNetClient.IsId(item.Id)).Select(item => item.Id).ToArray(), ratingTimeout.Token).ConfigureAwait(false);
                    var lookup = summaries.ToDictionary(item => item.Id);
                    foreach (var entry in entries)
                    {
                        if (lookup.TryGetValue(entry.Id, out var rating)) { entry.Rating = rating.Average; entry.RatingCount = rating.Count; }
                    }
                }
                catch (Exception) when (!token.IsCancellationRequested) { }
                token.ThrowIfCancellationRequested();
                result = BeatNetCatalog.LibraryPage(entries, filter, sorting, difficulties, offset, PageSize, focused);
            }
            else
            {
                result = await remote!.ConfigureAwait(false);
            }
            return (Page: result, Entries: entries);
        }, loaded =>
        {
            if (!IsOpen)
            {
                return;
            }
            client = source;
            libraryEntries = loaded.Entries;
            if (library && !libraryEntries.Any(item => item.Id == ArcadeSelection.LibrarySong)) { ArcadeSelection.SaveLibrarySong(string.Empty); }
            var result = loaded.Page;
            if (result.Items.Length == 0 && result.Total > 0 && offset > 0)
            {
                LoadPage(Math.Max(0, (result.Total - 1) / PageSize * PageSize));
                return;
            }
            foreach (var entry in result.Items)
            {
                if (!library && latest.TryGetValue(entry.Id, out var cached) && cached.Revision.Number < entry.Revision.Number)
                {
                    latest.Remove(entry.Id);
                }
            }
            ApplyPage(result);
        });
    }

    private void ApplyPage(CatalogPage result)
    {
        pageFrames.Clear();
        drawingPage = true;
        page = result;
        empty.gameObject.SetActive(page.Items.Length == 0);
        empty.text = query.Length > 0 ? "No matching songs" : library ? "Your library is empty" : "No beatmaps available";
        list.content.sizeDelta = new Vector2(ListWidth, Math.Max(508f, page.Items.Length * 86f - 8f));
        for (var index = 0; index < page.Items.Length; index++)
        {
            var row = index;
            pageFrames.Add(() =>
            {
                SetRowLabel(row);
                rows[row].gameObject.SetActive(true);
                rows[row].interactable = false;
                rowFades[row].Show();
            });
        }
        pageFrames.Add(() =>
        {
            FadeText(pageLabel, page.Total == 0 ? "0 songs" : $"{page.Offset + 1}-{page.Offset + page.Items.Length} of {page.Total}");
            FadeText(countLabel, $"{page.Total} SONG{(page.Total == 1 ? "" : "S")}");
            textFades[empty].Show();
            list.content.anchoredPosition = Vector2.zero;
        });
        pageFrames.Add(() =>
        {
            drawingPage = false;
            HideLoading();
            if (page.Items.Length > 0)
            {
                var index = Array.FindIndex(page.Items, item => item.Id == focusId);
                if (index < 0) { index = 0; }
                focusId = string.Empty;
                if (controller && !IsTyping && !tabs.IsMoving)
                {
                    events.SetSelectedGameObject(rows[index].gameObject);
                }
                SelectRow(index);
                RevealRow(index);
            }
            RefreshControls();
        });
        RefreshControls();
    }

    private void HideLoading()
    {
        loadingPage = false;
        listLoading.Hide();
        detailsLoading.Hide();
        sizeLoading.Hide();
        supportedLoading.Hide();
    }

    private bool IsInstalled(BeatmapEntry beatmap) => libraryEntries.Any(item => item.Id == beatmap.Id);

    private bool HasUpdate(BeatmapEntry beatmap)
    {
        if (!BeatNetClient.IsId(beatmap.Id))
        {
            return false;
        }
        var installed = libraryEntries.FirstOrDefault(item => item.Id == beatmap.Id);
        if (installed != null && Plugin.Accounts?.HasUpdate(beatmap.Id) == true) { return true; }
        var remote = latest.TryGetValue(beatmap.Id, out var entry) ? entry : library ? null : beatmap;
        return installed != null && remote != null && installed.Revision.Number < remote.Revision.Number;
    }

    private void SetRowLabel(int index)
    {
        var beatmap = page.Items[index];
        rowLabels[index].text = beatmap.Artist.Length == 0 ? beatmap.Title : $"{beatmap.Title} - {beatmap.Artist}";
        rowDetails[index].text = beatmap.Creator;
        rowStates[index].text = RowState(beatmap);
        ui.Style(rows[index], selected: index == selectedIndex);
        rowMarkers[index].gameObject.SetActive(index == selectedIndex);
    }

    private string RowState(BeatmapEntry beatmap)
    {
        var downloads = Plugin.Downloads;
        if (downloads?.Active?.Id == beatmap.Id)
        {
            return "DOWNLOADING";
        }
        var position = downloads?.Position(beatmap.Id) ?? 0;
        return position > 0 ? $"QUEUED {position}" : HasUpdate(beatmap) ? "UPDATE AVAILABLE" : IsInstalled(beatmap) ? "INSTALLED" : string.Empty;
    }

    private void SelectRow(int index, bool sound = false)
    {
        if (drawingPage || installing || index < 0 || index >= page.Items.Length || (index == selectedIndex && (pending != null || selected != null)))
        {
            return;
        }
        if (sound)
        {
            BeatNetSounds.Play(BeatNetSound.Confirm);
        }
        var beatmap = page.Items[index];
        if (library) { ArcadeSelection.SaveLibrarySong(beatmap.Id); }
        audioPreview.Stop();
        difficultyPopup.SetActive(false);
        var previousIndex = selectedIndex;
        selectedIndex = index;
        coverAfter = Time.unscaledTime + 0.1f;
        playDifficulty = string.Empty;
        if (previousIndex >= 0 && previousIndex != index)
        {
            ui.Style(rows[previousIndex]);
            rowMarkers[previousIndex].gameObject.SetActive(false);
        }
        ui.Style(rows[index], selected: true);
        rowMarkers[index].gameObject.SetActive(true);
        selected = null;
        CancelWork();
        sizeLoading.Hide();
        supportedLoading.Hide();
        ShowDetails(beatmap);
        status.text = string.Empty;
        if (library || latest.ContainsKey(beatmap.Id))
        {
            selected = latest.TryGetValue(beatmap.Id, out var cached) ? cached : beatmap;
            selected.Rating = beatmap.Rating;
            selected.RatingCount = beatmap.RatingCount;
            ShowDetails(selected);
            RefreshControls();
            return;
        }
        if (beatmap.Files.Length == 0)
        {
            sizeLoading.Show();
        }
        if (beatmap.Difficulties.Length == 0)
        {
            supportedLoading.Show();
        }
        Run(token => client!.Get(beatmap.Id, token), result =>
        {
            if (!IsOpen)
            {
                return;
            }
            latest[result.Id] = result;
            selected = result;
            sizeLoading.Hide();
            supportedLoading.Hide();
            page.Items[index] = result;
            SetRowLabel(index);
            ShowDetails(result);
            status.text = string.Empty;
        });
    }

    private void ShowDetails(BeatmapEntry beatmap)
    {
        ratings.Show(beatmap, library);
        if (detailCover.texture != null)
        {
            detailCover.texture = null;
            detailCover.gameObject.SetActive(false);
        }
        detailFrames.Clear();
        drawingDetails = true;
        QueueText(title, beatmap.Title);
        QueueText(artist, beatmap.Artist);
        QueueText(mapper, beatmap.Creator);
        var installedSize = libraryEntries.FirstOrDefault(item => item.Id == beatmap.Id)?.InstalledSize ?? beatmap.InstalledSize;
        var size = library && installedSize > 0 ? installedSize : beatmap.Files.Sum(file => file.Size);
        QueueText(sizeLabel, size > 0 ? (size / 1048576f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB" : string.Empty);
        var difficulties = library
            ? libraryEntries.FirstOrDefault(item => item.Id == beatmap.Id)?.Difficulties ?? beatmap.Difficulties
            : beatmap.Difficulties;
        var choices = library ? PlayableSongs() : Array.Empty<ArcadeSongDatabase.BeatmapItem>();
        for (var index = 0; index < difficultyNames.Count; index++)
        {
            var slot = index < difficulties.Length ? difficulties[index] : string.Empty;
            var song = choices.FirstOrDefault(item => item.BeatmapInfo.difficulty == slot);
            var level = song?.Beatmap.metadata.tagData.Level ?? (beatmap.Levels.TryGetValue(slot, out var number) ? number : 0);
            var name = song?.Beatmap.metadata.GetDifficulty(slot) ?? (beatmap.DifficultyLabels.TryGetValue(slot, out var label) ? label : slot);
            var row = index;
            detailFrames.Add(() =>
            {
                FadeText(difficultyNames[row], BeatNetScoreText.Difficulty(name));
                FadeText(difficultyLevels[row], slot.Length == 0 ? string.Empty : "<mspace=0.82em>" + level.ToString("00"));
                levelLabels[row].text = slot.Length == 0 ? string.Empty : "<mspace=0.80em>LV";
            });
        }
        detailFrames.Add(() =>
        {
            drawingDetails = false;
            RefreshControls();
        });
    }

    private void QueueText(TextMeshProUGUI text, string value) => detailFrames.Add(() => FadeText(text, value));

    private void FadeText(TextMeshProUGUI text, string value)
    {
        if (text.text != value || !textFades[text].IsVisible)
        {
            text.text = value;
            textFades[text].Show();
        }
    }

    private void LayoutDetails()
    {
        if (!IsOpen || title.text.Length == 0)
        {
            return;
        }
        if (title.havePropertiesChanged)
        {
            title.ForceMeshUpdate(true);
        }
        var height = Mathf.Clamp(title.GetRenderedValues(false).y, title.fontSize, 96f);
        var top = 242f + height + 2f;
        var mapperTop = -top - (artist.text.Length > 0 ? artist.rectTransform.rect.height : 0f);
        if (laidOutTitle == title.text && Mathf.Approximately(laidOutHeight, height)
            && Mathf.Approximately(mapper.rectTransform.anchoredPosition.y, mapperTop))
        {
            return;
        }
        laidOutTitle = title.text;
        laidOutHeight = height;
        artist.rectTransform.anchoredPosition = new Vector2(TextLeft, -top);
        mapper.rectTransform.anchoredPosition = new Vector2(TextLeft, mapperTop);
    }

    private void ReadRevisions()
    {
        var downloads = Plugin.Downloads;
        if (downloads == null) { return; }
        foreach (var entry in downloads.Latest)
        {
            if (!latest.TryGetValue(entry.Key, out var cached) || cached.Revision.Number <= entry.Value.Revision.Number)
            {
                latest[entry.Key] = entry.Value;
            }
        }
    }

    private void PollRevisions()
    {
        var version = Plugin.Accounts?.RevisionVersion ?? 0;
        var metadata = Plugin.Downloads?.RevisionVersion ?? 0;
        if (revisionVersion == version && metadataVersion == metadata) { return; }
        revisionVersion = version;
        metadataVersion = metadata;
        ReadRevisions();
        for (var index = 0; index < page.Items.Length; index++) { rowStates[index].text = RowState(page.Items[index]); }
        if (selected != null && HasUpdate(selected))
        {
            difficultyPopup.SetActive(false);
            if (latest.TryGetValue(selected.Id, out var remote))
            {
                selected = remote;
                ShowDetails(selected);
            }
        }
        RefreshControls();
    }

    private void TogglePreview()
    {
        if (audioPreview.IsActive)
        {
            audioPreview.Stop();
            return;
        }
        if (library || installing || pending != null || selected?.Preview == null || client == null)
        {
            return;
        }
        try
        {
            status.text = string.Empty;
            audioPreview.Play(client.PreviewUrl(selected));
        }
        catch (Exception error)
        {
            ShowError(error);
        }
    }

    private void Install()
    {
        if (selected == null || pending != null || client == null)
        {
            return;
        }
        audioPreview.Stop();
        var beatmap = selected;
        if (latest.TryGetValue(beatmap.Id, out var remote))
        {
            beatmap = remote;
        }
        Plugin.Downloads?.Add(beatmap);
        RefreshControls();
    }

    private void Uninstall()
    {
        if (!library || selected == null || installing || pending != null || Plugin.Downloads?.Count > 0)
        {
            return;
        }
        var beatmap = selected;
        installing = removing = true;
        CustomSongLoader.SelectAfterRemoval(libraryEntries.FirstOrDefault(item => item.Id == beatmap.Id)?.LocalPath ?? beatmap.LocalPath);
        progress = "Uninstalling";
        Run(token => Task.Run(() =>
        {
            installer.Uninstall(beatmap, token);
            return installer.Library();
        }, token), entries =>
        {
            reloadNeeded = true;
            if (IsOpen)
            {
                CustomSongLoader.Reload();
                reloadNeeded = false;
            }
            libraryEntries = entries;
            focusId = page.Items.Skip(selectedIndex + 1).Concat(page.Items.Take(selectedIndex).Reverse()).FirstOrDefault(item => item.Id != beatmap.Id)?.Id ?? string.Empty;
            ArcadeSelection.SaveLibrarySong(focusId);
            if (IsOpen)
            {
                LoadPage(page.Offset);
                status.text = "Uninstalled";
            }
        });
    }

    private ArcadeSongDatabase.BeatmapItem[] PlayableSongs()
    {
        if (selected == null || ArcadeSongDatabase.Instance == null)
        {
            return Array.Empty<ArcadeSongDatabase.BeatmapItem>();
        }
        var folder = libraryEntries.FirstOrDefault(item => item.Id == selected.Id)?.LocalPath;
        if (folder == null)
        {
            return Array.Empty<ArcadeSongDatabase.BeatmapItem>();
        }
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        var prefix = root + Path.DirectorySeparatorChar;
        return ArcadeSongDatabase.Instance.SongDatabase.Values.Where(item => item.CustomSong
            && (Path.GetFullPath(item.Song.CustomPath).Equals(root, StringComparison.OrdinalIgnoreCase)
                || Path.GetFullPath(item.Song.CustomPath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => Array.IndexOf(ArcadeSongDatabase.Instance.BeatmapIndex.Difficulties, item.BeatmapInfo.difficulty)).ToArray();
    }

    private void Play()
    {
        if (!library || selected == null || installing || pending != null)
        {
            return;
        }
        if (HasUpdate(selected))
        {
            BeatNetSounds.Play(BeatNetSound.Confirm);
            Install();
            return;
        }
        try
        {
            if (reloadNeeded)
            {
                reloadNeeded = false;
                CustomSongLoader.Reload();
            }
            var choices = PlayableSongs();
            var song = choices.FirstOrDefault(item => item.BeatmapInfo.difficulty == playDifficulty) ?? choices.FirstOrDefault();
            if (song == null)
            {
                status.text = "Cannot play this beatmap";
                return;
            }
            Close(finished: () => StartSong(song));
        }
        catch (Exception error)
        {
            CustomSongLoader.Logger?.LogWarning($"cannot play beatmap: {error.Message}");
            if (IsOpen)
            {
                status.text = "Cannot play this beatmap";
            }
        }
    }

    private void StartSong(ArcadeSongDatabase.BeatmapItem song)
    {
        try
        {
            RestoreInput(true);
            var database = ArcadeSongDatabase.Instance;
            database.SetCategory(database.SelectableCategories.FirstOrDefault(item => item.Name == "custom"));
            database.SetDifficulty(song.BeatmapInfo.difficulty);
            var index = database.IndexOfSong(song.Song);
            if (index >= 0)
            {
                ArcadeSongList.Instance.SetSelectedSongIndex(index);
            }
            database.PlaySong(song);
        }
        catch (Exception error)
        {
            CustomSongLoader.Logger?.LogWarning($"cannot play beatmap: {error.Message}");
        }
    }

    private void Run<T>(Func<CancellationToken, Task<T>> operation, Action<T> apply)
    {
        CancelWork();
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var task = Task.Run(() => operation(token), token);
        pending = task;
        finish = () => apply(task.GetAwaiter().GetResult());
        RefreshControls();
    }

    private void PollWork()
    {
        if (pending == null || !pending.IsCompleted)
        {
            return;
        }
        var done = finish;
        pending = null;
        finish = null;
        cancellation?.Dispose();
        cancellation = null;
        installing = false;
        removing = false;
        try
        {
            done?.Invoke();
        }
        catch (OperationCanceledException)
        {
            if (IsOpen && !closing)
            {
                status.text = "Request timed out / try again";
                if (loadingPage)
                {
                    empty.gameObject.SetActive(true);
                    FadeText(empty, "The collection could not load\nUse Search to try again");
                    HideLoading();
                }
            }
        }
        catch (Exception error)
        {
            ShowError(error);
        }
        RefreshControls();
    }

    private void RefreshControls()
    {
        foreach (var control in controls)
        {
            control.interactable = !installing;
        }
        controls[controls.Count - 1].interactable = true;
        filters.Sort.interactable = filters.Difficulty.interactable = !installing && !drawingPage && pending == null;
        foreach (var row in rows)
        {
            row.interactable = !installing && !drawingPage;
        }
        previous.interactable = !drawingPage && pending == null && page.Offset > 0;
        next.interactable = !drawingPage && pending == null && page.Offset + page.Items.Length < page.Total;
        ((BeatNetControl)previous).Refresh();
        ((BeatNetControl)next).Refresh();
        var installed = selected != null && IsInstalled(selected);
        LayoutDetails();
        var update = selected != null && HasUpdate(selected);
        ui.Style(exploreTab, !library);
        ui.Style(libraryTab, library);
        var installRect = (RectTransform)install.transform;
        installRect.anchoredPosition = new Vector2(TextLeft, library ? -766f : -812f);
        installRect.sizeDelta = new Vector2(TextWidth, 60f);
        var installLabel = install.GetComponentInChildren<TextMeshProUGUI>(true);
        installLabel.rectTransform.sizeDelta = new Vector2(installRect.sizeDelta.x - 32f, installRect.sizeDelta.y);
        installLabel.fontSize = 24f;
        var downloads = Plugin.Downloads;
        var downloading = selected != null && downloads?.Active?.Id == selected.Id;
        var queued = selected == null ? 0 : downloads?.Position(selected.Id) ?? 0;
        installLabel.text = downloading ? downloads!.Progress : queued > 0 ? $"Queued / {queued}" : update ? "Update" : installed ? "Installed" : selected == null ? "Choose a beatmap" : "Download";
        install.gameObject.SetActive(!library || update);
        install.interactable = selected != null && pending == null && !drawingDetails && !installing && (!installed || update)
            && downloads?.Contains(selected.Id) != true;
        downloadFill.gameObject.SetActive(downloading);
        if (downloading)
        {
            downloadText.text = downloads!.Progress;
            downloadFill.rectTransform.sizeDelta = new Vector2(TextWidth * Mathf.Clamp01(downloads.Fraction), 60f);
        }
        play.gameObject.SetActive(library && !update);
        ((RectTransform)play.transform).anchoredPosition = new Vector2(TextLeft, -766f);
        uninstall.gameObject.SetActive(library && installed);
        difficulty.gameObject.SetActive(library && installed);
        uninstall.interactable = installed && !drawingDetails && !installing && pending == null && !(downloads?.Count > 0);
        var choices = library && installed ? PlayableSongs() : Array.Empty<ArcadeSongDatabase.BeatmapItem>();
        if (playDifficulty.Length == 0 && choices.Length > 0)
        {
            playDifficulty = choices.FirstOrDefault(item => item.BeatmapInfo.difficulty == ArcadeSongDatabase.SelectedDifficulty)?.BeatmapInfo.difficulty
                ?? choices[0].BeatmapInfo.difficulty;
        }
        difficulty.interactable = choices.Length > 0 && !drawingDetails && !installing && pending == null;
        var currentSong = choices.FirstOrDefault(item => item.BeatmapInfo.difficulty == playDifficulty);
        difficulty.GetComponentInChildren<TextMeshProUGUI>(true).text = currentSong == null ? "No playable difficulty" : BeatNetScoreText.Difficulty(currentSong.Beatmap.metadata.GetDifficulty(playDifficulty));
        if (library)
        {
            SetScoreVisible(currentSong != null);
            if (currentSong != null)
            {
                var scores = FileStorage.highscores?.GetAllScores(currentSong.Path) ?? currentSong.Highscore;
                scores.TryGetValue(HighScoreList.GetModifiersLeaderboard(StorableBeatmapOptions.GetModifierMask()), out var record);
                var state = record?.cleared == true ? BeatNetScoreText.ClearState(true, record.IsFullCombo(), record.IsPerfectFullCombo()) : string.Empty;
                ShowScore(record?.score ?? 0, record?.accuracy ?? 0f, record != null && record.score > 0 && record.IsNoMiss(), record?.cleared == true, state, currentSong.Path);
            }
        }
        RefreshExploreScore();
        preview.gameObject.SetActive(!library);
        preview.interactable = !library && selected?.Preview != null && !drawingDetails && !installing && pending == null && client != null;
        play.interactable = installed && !update && !drawingDetails && !installing && pending == null && choices.Length > 0
            && downloads?.Contains(selected!.Id) != true;
        supported.gameObject.SetActive(true);
        supportedHeading.gameObject.SetActive(true);
        difficulty.GetComponent<RectTransform>().anchoredPosition = new Vector2(TextLeft, -696f);
        difficulty.GetComponent<RectTransform>().sizeDelta = new Vector2(TextWidth, 48f);
        difficulty.GetComponentInChildren<TextMeshProUGUI>(true).rectTransform.sizeDelta = new Vector2(TextWidth - 32f, 48f);
        updateLabel.text = update ? "Update available" : string.Empty;
        updateLabel.rectTransform.anchoredPosition = new Vector2(TextLeft, -878f);
        updateLabel.rectTransform.sizeDelta = new Vector2(TextWidth, 22f);
        updateLabel.alignment = TextAlignmentOptions.Right;
        ratings.Refresh(library, installing || drawingPage || drawingDetails || pending != null);
        var focused = events.currentSelectedGameObject?.GetComponent<Selectable>();
        if (controller && !closing && !tabs.IsMoving && !difficultyPopup.activeSelf && !filters.IsOpen && !keyboard.IsOpen
            && (focused == null || !focused.gameObject.activeInHierarchy || !focused.interactable || focused == exploreTab || focused == libraryTab))
        {
            var target = update && install.interactable ? install : play.gameObject.activeInHierarchy && play.interactable ? play
                : selectedIndex >= 0 && rows[selectedIndex].interactable ? rows[selectedIndex] : close;
            events.SetSelectedGameObject(target.gameObject);
        }
    }

    private void UpdateRating(BeatmapEntry beatmap)
    {
        foreach (var entry in libraryEntries.Concat(page.Items).Concat(latest.Values).Where(item => item.Id == beatmap.Id))
        {
            entry.Rating = beatmap.Rating;
            entry.RatingCount = beatmap.RatingCount;
        }
        if (library && filters.Sorting == "rating")
        {
            focusId = beatmap.Id;
            LoadPage(page.Offset);
        }
    }

    private void SetScoreVisible(bool active)
    {
        highscore.gameObject.SetActive(active);
        rank.gameObject.SetActive(active);
        cleared.gameObject.SetActive(active);
    }

    private void ShowScore(int score, float accuracy, bool noMiss, bool wasCleared, string label, string path)
    {
        if (path != scorePath)
        {
            scorePath = path;
            textFades[highscore].Clear();
            textFades[rank].Clear();
            textFades[cleared].Clear();
        }
        FadeText(highscore, "<mspace=0.8em>" + score.ToString("0000000"));
        FadeText(rank, score == 0 ? string.Empty : HighScoreScreen.GetLetterGradeArcade(accuracy, noMiss, wasCleared)
            .Replace("++", "<voffset=0.30em>+<voffset=-0.30em><space=-0.85em>+"));
        FadeText(cleared, label);
        LayoutScore();
    }

    private void RefreshExploreScore()
    {
        var modifiers = HighScoreList.GetModifiersLeaderboard(StorableBeatmapOptions.GetModifierMask());
        exploreScores.Tick(Time.unscaledTime, library || closing ? null : selected, modifiers);
        if (library) { return; }
        SetScoreVisible(selected != null && !closing);
        if (selected != null && !closing)
        {
            var record = exploreScores.Current;
            ShowScore(record.Score, record.Accuracy, record.NoMiss, record.Cleared, BeatNetScoreText.Difficulty(exploreScores.Label),
                "global:" + selected.Id + ":" + selected.Revision.Id + ":" + modifiers + ":" + record.Difficulty);
        }
        if (exploreScoreError != exploreScores.Error)
        {
            if (status.text == exploreScoreError || exploreScores.Error.Length > 0) { status.text = exploreScores.Error; }
            exploreScoreError = exploreScores.Error;
        }
    }

    private void LayoutScore()
    {
        if (laidOutScore == highscore.text && laidOutRank == rank.text && laidOutClear == cleared.text)
        {
            return;
        }
        highscore.ForceMeshUpdate();
        rank.ForceMeshUpdate();
        var size = highscore.textInfo.characterCount > 0 ? highscore.textInfo.characterInfo[0].pointSize : highscore.fontSize;
        cleared.fontSize = size * 13.64f / 48.1f;
        cleared.ForceMeshUpdate();
        GlyphBounds(highscore, out var scoreRight, out var scoreTop, out _);
        GlyphBounds(cleared, out var clearRight, out _, out var clearBottom);
        var scoreRect = highscore.rectTransform;
        var rankRect = rank.rectTransform;
        var clearRect = cleared.rectTransform;
        var scoreBaseline = highscore.textInfo.characterCount > 0 ? highscore.textInfo.characterInfo[0].baseLine : 0f;
        var rankBaseline = rank.textInfo.characterCount > 0 ? rank.textInfo.characterInfo[0].baseLine : 0f;
        rankRect.anchoredPosition = new Vector2(scoreRect.anchoredPosition.x + scoreRight * scoreRect.localScale.x + 12f,
            scoreRect.anchoredPosition.y + scoreBaseline * scoreRect.localScale.y - rankBaseline * rankRect.localScale.y);
        clearRect.anchoredPosition = new Vector2(scoreRect.anchoredPosition.x + scoreRight * scoreRect.localScale.x - clearRight * clearRect.localScale.x,
            scoreRect.anchoredPosition.y + scoreTop * scoreRect.localScale.y + size * 0.035f - clearBottom * clearRect.localScale.y);
        laidOutScore = highscore.text;
        laidOutRank = rank.text;
        laidOutClear = cleared.text;
    }

    private static void GlyphBounds(TextMeshProUGUI text, out float right, out float top, out float bottom)
    {
        right = top = bottom = 0f;
        var found = false;
        for (var index = 0; index < text.textInfo.characterCount; index++)
        {
            var character = text.textInfo.characterInfo[index];
            if (!character.isVisible || character.textElement == null)
            {
                continue;
            }
            var metrics = character.textElement.glyph.metrics;
            var glyphRight = character.origin + (metrics.horizontalBearingX + metrics.width) * character.scale;
            var glyphTop = character.baseLine + metrics.horizontalBearingY * character.scale;
            var glyphBottom = character.baseLine + (metrics.horizontalBearingY - metrics.height) * character.scale;
            right = found ? Mathf.Max(right, glyphRight) : glyphRight;
            top = found ? Mathf.Max(top, glyphTop) : glyphTop;
            bottom = found ? Mathf.Min(bottom, glyphBottom) : glyphBottom;
            found = true;
        }
    }

    private void ShowError(Exception error)
    {
        sizeLoading.Hide();
        supportedLoading.Hide();
        if (loadingPage)
        {
            HideLoading();
            empty.gameObject.SetActive(true);
        }
        status.text = error is InvalidDataException ? error.Message
            : error is HttpRequestException ? "Cannot reach BEATNET / check your connection or try again"
            : error is IOException || error is UnauthorizedAccessException ? "Cannot save the beatmap / check disk space and folder permissions"
            : "Cannot load this beatmap / try again";
        CustomSongLoader.Logger?.LogWarning("beatnet request failed");
        if (selectedIndex < 0)
        {
            FadeText(empty, "The collection could not load\nUse Search to try again");
        }
    }

    private RawImage CreateCover(Transform parent, float left, float top, float width, float height)
    {
        var image = ui.Rect(parent, "Cover", left, top, width, height).gameObject.AddComponent<RawImage>();
        ui.Tint(image, BeatNetColor.Cover);
        image.raycastTarget = false;
        image.gameObject.SetActive(false);
        return image;
    }

    private static void ShowCover(RawImage image, BeatNetFade animation, Texture2D? texture)
    {
        if (texture == null || image.texture == texture)
        {
            return;
        }
        image.texture = texture;
        var aspect = image.rectTransform.rect.width / image.rectTransform.rect.height;
        var source = texture.width / (float)texture.height;
        var width = source > aspect ? aspect / source : 1f;
        var height = source < aspect ? source / aspect : 1f;
        image.uvRect = new Rect((1f - width) * 0.5f, (1f - height) * 0.5f, width, height);
        image.gameObject.SetActive(true);
        animation.Show();
    }

    private BeatmapEntry CoverEntry(BeatmapEntry entry)
    {
        var local = libraryEntries.FirstOrDefault(item => item.Id == entry.Id);
        return local?.CoverPath != null && (library || local.Revision.Id == entry.Revision.Id) ? local : entry;
    }

    private void UpdateCovers()
    {
        if (!IsOpen || closing || loadingPage || covers == null)
        {
            return;
        }
        covers.Tick();
        if (selectedIndex >= 0 && selectedIndex < page.Items.Length && Time.unscaledTime >= coverAfter)
        {
            ShowCover(detailCover, detailCoverFade, covers.Get(CoverEntry(page.Items[selectedIndex])));
        }
    }

    private void ResetCovers()
    {
        detailCover.texture = null;
        detailCover.gameObject.SetActive(false);
        covers.Reset();
    }

    private void PollDownloads()
    {
        var downloads = Plugin.Downloads;
        if (downloads == null || !IsOpen || closing)
        {
            return;
        }
        if (downloads.Version != downloadVersion && pending == null && !drawingPage)
        {
            downloadVersion = downloads.Version;
            if (downloads.Entries.Count > 0)
            {
                libraryEntries = downloads.Entries;
            }
            if (library)
            {
                LoadPage(page.Offset);
            }
            status.text = downloads.Error;
        }
        var stamp = $"{downloads.Active?.Id}/{downloads.Progress}/{downloads.Count}/{downloads.Version}";
        if (stamp == downloadStamp)
        {
            return;
        }
        downloadStamp = stamp;
        for (var index = 0; index < page.Items.Length; index++)
        {
            rowStates[index].text = RowState(page.Items[index]);
        }
        RefreshControls();
        if (downloads.Count > 0)
        {
            status.text = $"{downloads.Active?.Title ?? "Download queued"} / {downloads.Progress}"
                + (downloads.Count > 1 ? $" / {downloads.Count - 1} queued" : string.Empty);
        }
    }

    private void Update()
    {
        backgroundPerspective?.Tick();
        windowPerspective.Set(difficultyPopup.activeSelf || accountPanel.IsOpen || filters.IsOpen ? 0.8f : 1.4f);
        windowPerspective.Tick();
        difficultyPerspective.Tick();
        ui.RefreshTheme();
        accountPanel.Tick();
        filters.Tick();
        ratings.Tick();
        if (ratingError != ratings.Error)
        {
            if (status.text == ratingError || ratings.Error.Length > 0) { status.text = ratings.Error; }
            ratingError = ratings.Error;
        }
        rhythm.Tick();
        PollWork();
        PollRevisions();
        PollDownloads();
        UpdateCovers();
        try
        {
            if (pageFrames.IsPending)
            {
                pageFrames.Tick();
            }
            else if (detailFrames.IsPending)
            {
                detailFrames.Tick();
            }
        }
        catch (Exception error)
        {
            pageFrames.Clear();
            detailFrames.Clear();
            drawingPage = drawingDetails = false;
            ShowError(error);
            RefreshControls();
        }
        audioPreview.Tick();
        RefreshExploreScore();
        preview.GetComponentInChildren<TextMeshProUGUI>(true).text = audioPreview.IsLoading ? "Cancel preview" : audioPreview.IsActive ? "Stop preview" : "Preview";
        if (audioPreview.Error.Length > 0)
        {
            status.text = audioPreview.Error;
        }
        if (screenWidth != Screen.width || screenHeight != Screen.height)
        {
            screenWidth = Screen.width;
            screenHeight = Screen.height;
            scaler.matchWidthOrHeight = Screen.width / (float)Math.Max(1, Screen.height) < 1640f / 940f ? 0f : 1f;
        }
        if (installing)
        {
            status.text = removing ? progress : string.Empty;
        }
    }

    private void LateUpdate()
    {
        if (closing)
        {
            return;
        }
        LayoutDetails();
        rhythm.LateTick();
        if (!sloganAligned)
        {
            heading.ForceMeshUpdate(true);
            var count = heading.textInfo.characterCount;
            if (count > 0)
            {
                var right = heading.rectTransform.anchoredPosition.x + heading.textInfo.characterInfo[count - 1].topRight.x + 8f;
                var position = slogan.rectTransform.anchoredPosition;
                position.x = right - slogan.rectTransform.rect.width;
                slogan.rectTransform.anchoredPosition = position;
                sloganAligned = true;
            }
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = !controller || IsTyping;
        GetComponent<GraphicRaycaster>().enabled = true;
        if (!controller && fade.interactable && (!difficultyPopup.activeSelf || difficultyMotion.IsReady)
            && (!keyboard.IsOpen || keyboard.IsReady) && (!accountPanel.IsOpen || accountPanel.IsReady))
        {
            pointerModule.ProcessPointer();
        }
    }

    internal void HandleNavigation()
    {
        if (closing || tabs.IsMoving || !fade.interactable || difficultyPopup.activeSelf && !difficultyMotion.IsReady
            || filters.IsOpen && !filters.IsReady
            || keyboard.IsOpen && !keyboard.IsReady || !Application.isFocused || Time.frameCount == shownFrame)
        {
            return;
        }
        var mouse = (Vector3)pointerInput.mousePosition;
        var wheel = pointerInput.mouseScrollDelta.y;
        if ((mouse - mousePosition).sqrMagnitude > 4f || pointerInput.GetMouseButtonDown(0) || wheel != 0f)
        {
            UseKeyboard();
        }
        mousePosition = mouse;
        var direction = Vector2Int.zero;
        var submit = false;
        var controllerMove = false;
        if (ReInput.isReady)
        {
            foreach (var joystick in ReInput.players.GetPlayer(0).controllers.Joysticks)
            {
                var pad = joystick.GetTemplate<IGamepadTemplate>();
                if (pad == null)
                {
                    continue;
                }
                var stick = pad.leftStick.value;
                var movement = new Vector2(
                    pad.dPad.right.value ? 1f : pad.dPad.left.value ? -1f : stick.x,
                    pad.dPad.up.value ? 1f : pad.dPad.down.value ? -1f : stick.y);
                var moving = movement.sqrMagnitude >= 0.25f;
                var scrollAxis = pad.rightStick.value.y;
                var scrolling = Mathf.Abs(scrollAxis) > 0.25f;
                var active = moving || scrolling || pad.a.justPressed || pad.b.justPressed
                    || pad.leftBumper.justPressed || pad.rightBumper.justPressed;
                if (active)
                {
                    JeffBezosController.currentControllerType = ControllerType.Joystick;
                    JeffBezosController.currentControllerId = joystick.id;
                    SetController(true);
                }
                if (pad.b.justPressed)
                {
                    Back();
                    return;
                }
                if (moving)
                {
                    direction = Mathf.Abs(movement.x) > Mathf.Abs(movement.y)
                        ? new Vector2Int(movement.x > 0f ? 1 : -1, 0)
                        : new Vector2Int(0, movement.y > 0f ? 1 : -1);
                    controllerMove = true;
                }
                if (scrolling && !IsTyping && !difficultyPopup.activeSelf && !filters.IsOpen)
                {
                    list.OnScroll(new PointerEventData(events)
                    {
                        scrollDelta = new Vector2(0f, scrollAxis * Time.unscaledDeltaTime * 480f / list.scrollSensitivity),
                    });
                }
                submit |= pad.a.justPressed;
                if (pad.leftBumper.justPressed && !difficultyPopup.activeSelf && !filters.IsOpen && !IsTyping)
                {
                    SetLibrary(false);
                }
                if (pad.rightBumper.justPressed && !difficultyPopup.activeSelf && !filters.IsOpen && !IsTyping)
                {
                    SetLibrary(true);
                }
            }
        }
        var tab = UnityEngine.Input.GetKeyDown(KeyCode.Tab);
        if (accountPanel.IsOpen)
        {
            if (!accountPanel.IsReady) { return; }
            if (tab) { accountPanel.Navigate(Vector2Int.zero, UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift) ? -1 : 1); }
            if (!accountPanel.IsTyping && !controllerMove)
            {
                direction = new Vector2Int(UnityEngine.Input.GetKey(KeyCode.RightArrow) ? 1 : UnityEngine.Input.GetKey(KeyCode.LeftArrow) ? -1 : 0,
                    UnityEngine.Input.GetKey(KeyCode.UpArrow) ? 1 : UnityEngine.Input.GetKey(KeyCode.DownArrow) ? -1 : 0);
            }
            var popupMove = direction.x + direction.y * 2;
            if (popupMove != 0 && (popupMove != lastDirection || Time.unscaledTime >= nextMove))
            {
                accountPanel.Navigate(direction, 0, controllerMove);
                nextMove = Time.unscaledTime + (popupMove == lastDirection ? 0.12f : 0.35f);
            }
            lastDirection = popupMove;
            if (submit || UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)) { accountPanel.Activate(controller); }
            return;
        }
        if (search.isFocused && !keyboard.IsOpen)
        {
            if (submit)
            {
                search.DeactivateInputField();
                if (controller)
                {
                    keyboard.Open();
                }
                else
                {
                    events.SetSelectedGameObject(controls[1].gameObject);
                    Search();
                }
                return;
            }
            if (!controllerMove && !tab)
            {
                return;
            }
            search.DeactivateInputField();
        }
        var keyDirection = new Vector2Int(
            UnityEngine.Input.GetKey(KeyCode.RightArrow) || UnityEngine.Input.GetKey(KeyCode.D) ? 1
                : UnityEngine.Input.GetKey(KeyCode.LeftArrow) || UnityEngine.Input.GetKey(KeyCode.A) ? -1 : 0,
            UnityEngine.Input.GetKey(KeyCode.UpArrow) || UnityEngine.Input.GetKey(KeyCode.W) ? 1
                : UnityEngine.Input.GetKey(KeyCode.DownArrow) || UnityEngine.Input.GetKey(KeyCode.S) ? -1 : 0);
        var keySubmit = UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)
            || UnityEngine.Input.GetKeyDown(KeyCode.Space);
        if (keyDirection != Vector2Int.zero || tab || keySubmit)
        {
            UseKeyboard();
        }
        if (keyDirection != Vector2Int.zero)
        {
            direction = keyDirection.y == 0 ? keyDirection : new Vector2Int(0, keyDirection.y);
        }
        if (tab)
        {
            var backwards = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            Navigate(Vector2Int.zero, backwards ? -1 : 1);
        }
        var move = direction.x + direction.y * 2;
        if (move == 0 && lastDirection != 0 && events.currentSelectedGameObject == ratings.Slider.gameObject) { ratings.Commit(); }
        if (move != 0 && (move != lastDirection || Time.unscaledTime >= nextMove))
        {
            Navigate(direction);
            nextMove = Time.unscaledTime + (move == lastDirection ? 0.12f : 0.35f);
        }
        lastDirection = move;
        submit |= keySubmit;
        if (submit)
        {
            var control = events.currentSelectedGameObject?.GetComponent<Selectable>();
            if (control == null || !control.interactable)
            {
                Navigate(Vector2Int.zero, 1);
            }
            else if (control is Button button)
            {
                button.OnSubmit(new BaseEventData(events));
            }
            else if (control == ratings.Slider) { ratings.Commit(); }
            else if (control == search)
            {
                if (controller)
                {
                    keyboard.Open();
                }
                else
                {
                    BeatNetSounds.Play(BeatNetSound.Confirm);
                    search.ActivateInputField();
                }
            }
        }
    }

    private void UseKeyboard()
    {
        JeffBezosController.currentControllerType = ControllerType.Keyboard;
        JeffBezosController.currentControllerId = 0;
        SetController(false);
    }

    private void ScrollList(float distance)
    {
        var overflow = list.content.rect.height - list.viewport.rect.height;
        if (overflow > 0f)
        {
            list.StopMovement();
            list.verticalNormalizedPosition = Mathf.Clamp01(list.verticalNormalizedPosition + distance / overflow);
        }
    }

    private void RevealRow(int index)
    {
        var top = index * 86f;
        var offset = list.content.anchoredPosition.y;
        var bottom = top + 78f;
        if (top < offset)
        {
            ScrollList(offset - top);
        }
        else if (bottom > offset + list.viewport.rect.height)
        {
            ScrollList(offset + list.viewport.rect.height - bottom);
        }
    }

    private void Navigate(Vector2Int direction, int step = 0)
    {
        if (filters.IsOpen) { filters.Navigate(direction, step); return; }
        if (step == 0 && direction.x != 0 && events.currentSelectedGameObject == ratings.Slider.gameObject)
        {
            ratings.Adjust(direction.x);
            return;
        }
        if (keyboard.IsOpen)
        {
            keyboard.Navigate(direction, step);
            return;
        }
        var current = events.currentSelectedGameObject?.GetComponent<Selectable>();
        if (difficultyPopup.activeSelf)
        {
            var choices = difficultyChoices.Where(button => button.gameObject.activeSelf).Cast<Selectable>().Append(difficultyBack).ToList();
            var points = choices.Select(control =>
            {
                var rect = (RectTransform)control.transform;
                var center = difficultyDialog.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
                return new NavigationPoint(center.x, center.y, control.interactable);
            }).ToArray();
            var choice = step != 0 ? BeatNetNavigation.Step(points, choices.IndexOf(current!), step)
                : BeatNetNavigation.Find(points, choices.IndexOf(current!), direction.x, direction.y, preferAligned: true);
            if (choice >= 0 && choices[choice] != current)
            {
                BeatNetSounds.Move(direction, step);
                events.SetSelectedGameObject(choices[choice].gameObject);
            }
            return;
        }
        Selectable? target = null;
        var row = current is Button button ? rows.IndexOf(button) : -1;
        if (step == 0 && direction.x > 0 && row >= 0)
        {
            target = difficulty.gameObject.activeInHierarchy && difficulty.interactable ? difficulty
                : install.gameObject.activeInHierarchy && install.interactable ? install
                : play.gameObject.activeInHierarchy && play.interactable ? play
                : uninstall.gameObject.activeInHierarchy && uninstall.interactable ? uninstall : close;
        }
        else if (step == 0 && direction.x < 0 && (current == install || current == play || current == uninstall || current == difficulty) && selectedIndex >= 0)
        {
            target = rows[selectedIndex];
        }
        else
        {
            var points = new List<NavigationPoint>(controls.Count);
            foreach (var control in controls)
            {
                var rect = (RectTransform)control.transform;
                var center = window.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
                points.Add(new NavigationPoint(center.x, center.y, control.gameObject.activeInHierarchy && control.interactable
                    && (!controller || control != exploreTab && control != libraryTab)));
            }
            var index = step != 0 ? BeatNetNavigation.Step(points, controls.IndexOf(current!), step)
                : BeatNetNavigation.Find(points, controls.IndexOf(current!), direction.x, direction.y);
            if (index >= 0)
            {
                target = controls[index];
            }
        }
        if (target == null || target == current)
        {
            return;
        }
        var selectedRow = target is Button rowButton ? rows.IndexOf(rowButton) : -1;
        if (selectedRow >= 0 && selectedRow != selectedIndex)
        {
            BeatNetSounds.Play(BeatNetSound.Confirm);
        }
        else
        {
            BeatNetSounds.Move(direction, step);
        }
        events.SetSelectedGameObject(target.gameObject);
        if (selectedRow >= 0)
        {
            RevealRow(selectedRow);
            SelectRow(selectedRow);
        }
    }

    private void CancelWork()
    {
        cancellation?.Cancel();
        if (pending != null)
        {
            _ = pending.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        cancellation?.Dispose();
        cancellation = null;
        pending = null;
        finish = null;
    }

    private void OnDisable()
    {
        if (covers != null)
        {
            ResetCovers();
        }
        pageFrames.Clear();
        detailFrames.Clear();
        drawingPage = drawingDetails = false;
        visible = false;
        exploreScores?.Dispose();
        closing = false;
        closeFrame = Time.frameCount;
        if (inputOwner != null)
        {
            RestorePerspective();
            audioPreview.Stop();
            keyboard.Hide();
            accountPanel.Hide();
            filters.Hide();
            difficultyPopup.SetActive(false);
            cancellation?.Cancel();
            search.DeactivateInputField();
            RestoreCursor();
        }
    }

    private void OnDestroy()
    {
        exploreScores?.Dispose();
        covers?.Dispose();
        firstCancellation.Cancel();
        firstCancellation.Dispose();
        CancelWork();
        RestoreInput(true);
        if (clientReady != null)
        {
            _ = clientReady.ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    task.Result.Dispose();
                }
                else
                {
                    _ = task.Exception;
                }
            }, TaskScheduler.Default);
        }
        audioPreview.Dispose();
        ui.Dispose();
    }
}

internal sealed class BeatNetFrames
{
    private readonly Queue<Action> steps = new();

    internal bool IsPending => steps.Count > 0;

    internal void Add(Action step) => steps.Enqueue(step);

    internal void Clear()
    {
        steps.Clear();
    }

    internal void Tick()
    {
        if (steps.Count == 0)
        {
            return;
        }
        var step = steps.Dequeue();
        step();
    }
}



