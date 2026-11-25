using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SeaPowerKeel.Loading;
using UnityEngine;

namespace SeaPowerKeel;

/// <summary>
/// The cards at the bottom left of the main menu, drawn by the game (his rule 2026-11-12: every window is drawn by the game).
/// <para>
/// The loading overlay is an own window only because the game can stand still while a mod starts. When it has glided to its
/// summary spot it hands over: Keel draws the same summary card there and the overlay fades out. If something changed since the
/// last start, the card divides like a cell: it bulges, pinches and splits, and What's new grows from the bottom up in the
/// corner, pushing the Keel card above it - never over the main menu's EXIT (docs/idea-news-cell-split.md).
/// </para>
/// </summary>
internal sealed partial class KeelHost
{
    private const float SplitSeconds = 1.15f, SplitDelay = 0.35f, CardGap = 8f;
    private const float VersionTextTop = 0.975f;          // the game's version text starts near 0.98 of the screen height
    private const int CardErrorsBeforeNewsCloses = 3;      // never an error every frame

    private bool cardOn, cardConfirmed, cardHidden, cardFailed;
    private bool summaryCollapsed;
    private float cardSince;
    private long cardSceneTicks;
    private Rect cardHome;                    // GUI units: where the overlay left the card (or where it would be)
    private Rect cardRect, creditRect, cardCloseRect;        // where it is drawn now
    private Rect getItRect;                  // the card's "Get it" button, when a required item is missing
    private RequiredItem getItItem;
    private float cardTop = float.NaN;        // its top, following the stack after the split
    private float splitStart = -1f;
    private int stackMods = NewsMods;
    private int cardErrors;

    private Texture2D circleTex;
    private GUIStyle cardHead, cardText, cardSmall, cardCredit;
    private float cardStyleUnit = -1f;

    private static readonly UnityEngine.Color CardBack = Rgb(KeelPalette.Panel, 0.97f),
        CardGreen = Rgb(KeelPalette.Green), CardAmber = Rgb(KeelPalette.Amber),
        CardText = Rgb(KeelPalette.Text), CardDim = Rgb(KeelPalette.Muted);

    // ---- what is up -------------------------------------------------------------------------------------------------------

    private bool CardVisible
    {
        get
        {
            if (!cardOn || cardHidden)
                return false;
            // It belongs to the main menu's top page, and comes back to it every time (his word 2026-11-12: always visible
            // there): away while a mission or another scene runs, back once the menu is up again (also after a Mods menu reload).
            if (LoadingWindow.MissionRunning)
                return false;
            if (LoadingWindow.SceneLoadedTicks != cardSceneTicks)
            {
                if (!GameState.MenuUp() || !GameState.StartupDone())
                    return false;
                cardSceneTicks = LoadingWindow.SceneLoadedTicks;
            }
            return !LoadingWindow.OnMenuSubPage;
        }
    }

    /// <summary>The top of whatever stands at the bottom left (cards), for the notice box above it; NaN when nothing does.</summary>
    private float StackTop => CardVisible ? cardRect.y : NewsVisible ? newsRect.y : float.NaN;

    // ---- per frame ----------------------------------------------------------------------------------------------------------

    /// <summary>Where the cards go this frame, in GUI units.</summary>
    private struct CardSpace
    {
        public float X, H, Width, Bottom, NewsH;
        public bool NewsUp;
    }

    /// <summary>Called by OnGUI when the report window is not open.</summary>
    private void DrawMenuCards(float screenW, float screenH)
    {
        try
        {
            TakeOverSummary();
            // While the loading window is still up and on its way to the corner, What's new waits: it is born out of that card.
            bool overlayOnItsWay = LoadingWindow.Shown && !LoadingWindow.SummaryReady && !LoadingWindow.OverlayGone;
            bool card = CardVisible, newsUp = !overlayOnItsWay && NewsVisible;
            if (!card && !newsUp)
                return;
            EnsureCardStyles();
            // Both cards stay until the player clicks them away (his word 2026-11-12: "no fading on their own"): each has an × that
            // closes it. Nothing times out, no right click.
            CardSpace space = Room(card, newsUp, screenW, screenH);
            DrawCards(card, space);
            // The overlay fades out once the game has drawn the card (until then it covers the spot, so nothing blinks).
            if (card && !cardConfirmed && Event.current.type == EventType.Repaint)
            {
                cardConfirmed = true;
                LoadingWindow.GameDrawsSummary = true;
                log.LogInfo("Menu cards: the game draws the Keel card now; the loading window fades out.");
            }
            HandleCardEvents();
        }
        catch (Exception ex)
        {
            if (!cardConfirmed && cardOn)
            {
                cardOn = false;             // the overlay keeps its own summary
                cardFailed = true;
            }
            if (cardErrors++ == 0)
                log.LogWarning($"Menu cards: {ex}");
            cardHidden = true;
            if (cardErrors > CardErrorsBeforeNewsCloses)
                newsClosed = true;
        }
    }

    // His hard rule (2026-11-12): both cards always inside the grey menu panel, between EXIT and the version text, at any
    // resolution and any length. Width = the panel's minus a margin each side; height = as many mods as fit, else a one-line
    // What's new; the text is cut to what fits.
    private CardSpace Room(bool card, bool newsUp, float screenW, float screenH)
    {
        Rect home = cardOn ? cardHome : DefaultHome();
        if (cardFailed && LoadingWindow.SummaryReady && !LoadingWindow.OverlayGone)
            home.y -= home.height + CardGap;        // the overlay keeps its own card in the corner: What's new stands above it
        MeasureMenu(screenH);
        var space = new CardSpace
        {
            X = home.x, H = summaryCollapsed ? 24f : home.height, NewsUp = newsUp,
            Bottom = Mathf.Min(home.yMax, screenH * VersionTextTop - 6f),
            Width = Mathf.Min(home.width, Mathf.Max(120f, PanelRight(screenW) - 2f * home.x)),
        };
        if (!newsUp)
            return space;
        float room = space.Bottom - (exitBottom + 10f) - (card ? CardGap + space.H : 0f);
        stackMods = Mathf.Min(NewsMods, news.Count);
        space.NewsH = NewsHeight(space.Width, stackMods);
        while (stackMods > 0 && space.NewsH > room)
            space.NewsH = NewsHeight(space.Width, --stackMods);       // 0 = the one-line card: title and "N changed - click"
        if (space.NewsH > room)
            space.NewsUp = false;                                      // no room at all: the report still has the list
        return space;
    }

    private void DrawCards(bool card, CardSpace space)
    {
        float now = Time.realtimeSinceStartup;
        if (card && space.NewsUp && splitStart < 0f && now - cardSince > SplitDelay)
        {
            splitStart = now;
            log.LogInfo("Menu cards: What's new splits off the Keel card.");          // a signal for tests (frames of the split)
        }
        float p = splitStart < 0f ? (space.NewsUp && !card ? 1f : 0f) : Mathf.Clamp01((now - splitStart) / SplitSeconds);
        if (card && !summaryCollapsed && space.NewsUp && p < 1f)
        {
            DrawSplit(p, new Rect(space.X, space.Bottom - space.H, space.Width, space.H), space.Width, space.Bottom, space.NewsH);
            return;
        }
        // Settled: What's new in the corner, the Keel card above it; the card glides back when What's new closes.
        if (space.NewsUp)
        {
            newsRect = new Rect(space.X, space.Bottom - space.NewsH, space.Width, space.NewsH);
            DrawBody(newsRect);
            DrawNewsContent(newsRect, stackMods, 1f);
        }
        if (card)
        {
            float target = space.NewsUp ? newsRect.y - CardGap - space.H : space.Bottom - space.H;
            cardTop = float.IsNaN(cardTop) ? target : Mathf.Lerp(cardTop, target, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            DrawSummaryCard(new Rect(space.X, cardTop, space.Width, space.H));
        }
    }

    /// <summary>The overlay sits on its spot: Keel draws the card there from now on, and tells the overlay once it has.</summary>
    private void TakeOverSummary()
    {
        if (cardOn || cardFailed || !LoadingWindow.Shown || !done || !GameState.MenuUp()) return;
        if (!LoadingWindow.SummaryReady && !LoadingWindow.OverlayGone) return;
        cardOn = true;
        cardSince = Time.realtimeSinceStartup;
        cardSceneTicks = LoadingWindow.SceneLoadedTicks;
        // The player can dismiss the native loading window before it publishes a summary position.
        summaryCollapsed = !LoadingWindow.SummaryReady;
        cardHome = summaryCollapsed ? DefaultHome() : new Rect(LoadingWindow.SummaryX / guiScale,
            LoadingWindow.SummaryY / guiScale, LoadingWindow.SummaryW / guiScale, LoadingWindow.SummaryH / guiScale);
        cardTop = cardHome.y;
    }

    /// <summary>Where the summary card sits when there is no overlay to hand over (loading window switched off): the overlay's layout.</summary>
    private Rect DefaultHome()
    {
        float px = Screen.height, s = Overlay.Layout.Scale(Screen.height, 1f);
        float w = Overlay.Layout.SummaryWidth * s, h = Overlay.Layout.SummaryHeight * s, x = Overlay.Layout.SummaryMargin * s;
        float y = px - px * (float)Overlay.Layout.SummaryBottomShare - h;
        return new Rect(x / guiScale, y / guiScale, w / guiScale, h / guiScale);
    }

    // ---- the split -----------------------------------------------------------------------------------------------------------

    private static float Smooth(float t) => t * t * (3f - 2f * t);
    private static float EaseOutBack(float t) { const float c = 1.4f; t -= 1f; return 1f + (c + 1f) * t * t * t + c * t * t; }

    /// <summary>
    /// One frame of the division, p from 0 to 1, as a budding cell:
    /// A (0-0.3): the Keel card lifts and a round bud swells out of its lower edge, with a few small bubbles.
    /// B (0.3-0.65): the card rises further, the bud flattens into a small card on the corner; the neck between them, a column of
    /// bubbles, thins and tears, and a few drops pop off.
    /// C (0.65-1): the bud grows from the bottom up into What's new (springy), pushing the Keel card above it; its text fades in.
    /// Everything stays above the card's old lower edge (below it are the game's version text and the screen's edge).
    /// </summary>
    private void DrawSplit(float p, Rect home, float width, float bottom, float newsH)
    {
        float x = home.x, h = home.height, w0 = home.width, y0 = home.y, cx = x + w0 / 2;
        if (p < 0.3f)
        {
            float q = p / 0.3f, e = Smooth(q), swell = Mathf.Sin(q * Mathf.PI * 0.5f);
            var upper = new Rect(x, y0 - 0.45f * h * e, w0, h);
            float r = 0.42f * h * swell;
            Circle(cx, upper.yMax + r * 0.1f, r);                                   // the bud
            for (int i = -1; i <= 1; i += 2)                                               // bubbles beside it
                Circle(cx + i * (r + 0.12f * h * swell), upper.yMax + 0.05f * h, 0.12f * h * swell);
            DrawSummaryCard(upper);
            return;
        }
        if (p < 0.65f)
        {
            float q = (p - 0.3f) / 0.35f, e = Smooth(q);
            var upper = new Rect(x, y0 - 0.45f * h - 0.30f * h * e, w0, h);
            float bw = Mathf.Lerp(0.84f * h, 0.55f * w0, e), bh = Mathf.Lerp(0.84f * h, 0.5f * h, e);
            var bud = new Rect(cx - bw / 2, bottom - bh, bw, bh);
            DrawBody(bud);
            // the neck: a column of bubbles between the two, thinning to nothing
            float neckW = Mathf.Min(bw, w0) * 0.6f * (1f - Smooth(Mathf.Clamp01((q - 0.15f) / 0.85f)));
            if (neckW > 1f)
            {
                float step = Mathf.Max(2f, neckW * 0.3f);
                for (float cy = upper.yMax - neckW * 0.3f; cy <= bud.y + neckW * 0.3f; cy += step)
                    for (float bx = cx - neckW / 2 + neckW * 0.3f; bx <= cx + neckW / 2 - neckW * 0.3f + 0.1f; bx += step)
                        Circle(bx, cy, neckW * 0.3f);
            }
            if (q > 0.8f)
            {
                float d = (q - 0.8f) / 0.2f, mid = (upper.yMax + bud.y) / 2;      // drops popping off where it tore
                for (int i = -1; i <= 1; i++)
                    Circle(cx + i * w0 * 0.1f * (1 + d), mid + (i == 0 ? -1 : 1) * h * 0.06f * d, h * 0.045f * (1 - d));
            }
            DrawSummaryCard(upper);
            return;
        }
        {
            float q = (p - 0.65f) / 0.35f, e = EaseOutBack(q), s = Smooth(q);
            float lw = Mathf.Lerp(0.55f * w0, width, s), lh = Mathf.Max(0.3f * h, Mathf.LerpUnclamped(0.5f * h, newsH, e));
            var lower = new Rect(Mathf.Lerp(cx - 0.275f * w0, x, s), bottom - lh, lw, lh);
            float gap = Mathf.Lerp(0.25f * h, CardGap, s);
            var upper = new Rect(x, lower.y - gap - h, Mathf.Lerp(w0, width, s), h);
            DrawBody(lower);
            newsRect = lower;
            DrawNewsContent(lower, stackMods, Mathf.Clamp01((q - 0.78f) / 0.22f));      // the text once the card is almost its full width
            DrawSummaryCard(upper);
            cardTop = upper.y;
        }
    }

    // ---- drawing --------------------------------------------------------------------------------------------------------------

    private void DrawBody(Rect r)
    {
        if (Event.current.type != EventType.Repaint)
            return;
        UnityEngine.Color was = GUI.color;
        GUI.color = UnityEngine.Color.white;
        GUI.DrawTexture(r, Tint(CardBack));
        GUI.color = was;
    }

    private void Circle(float cx, float cy, float r)
    {
        if (r <= 0.3f || Event.current.type != EventType.Repaint)
            return;
        UnityEngine.Color was = GUI.color;
        GUI.color = CardBack;
        GUI.DrawTexture(new Rect(cx - r, cy - r, 2 * r, 2 * r), circleTex);
        GUI.color = was;
    }

    private static Rect CloseSpot(Rect card) => new Rect(card.xMax - 24, card.y + 3, 21, 18);

    /// <summary>The Keel card: what Keel did, how long the game took, what to look at (the overlay's summary, drawn by the game).</summary>
    private void DrawSummaryCard(Rect r)
    {
        if (summaryCollapsed)
        {
            // The report button has its own × too (his rule: every card closes with a visible ×); the hotkey still opens the report.
            cardRect = new Rect(r.x, r.y, Mathf.Min(136f, r.width), r.height);
            GUI.Box(new Rect(cardRect.x, cardRect.y, cardRect.width - 26f, cardRect.height), "Keel report", buttonStyle);
            EnsureNewsStyles();
            var spot = CloseSpot(cardRect);
            GUI.Label(spot, "×", newsClose);
            cardCloseRect = new Rect(spot.x - 4, cardRect.y, spot.width + 8, cardRect.height);
            return;
        }
        cardRect = r;
        if (!cardOn)
            return;
        Progress.Snapshot s = LoadingWindow.Progress?.Take(0);
        if (s == null)
            return;
        float u = LoadingWindow.SummaryScale / guiScale;
        UnityEngine.Color was = GUI.color;
        DrawBody(r);
        string alert = LoadingWindow.Alert;
        bool amber = alert != null || s.HasProblems;
        GUI.color = UnityEngine.Color.white;
        if (Event.current.type == EventType.Repaint)
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, Overlay.Layout.HeaderHeight * u), headerBack);
        float pad = Overlay.Layout.Pad * u, y = r.y, lineH = Overlay.Layout.LineHeight * u, tw = r.width - 2 * pad;
        // An × top right closes it, like What's new (his word 2026-11-12: "just add a x to both, no right click").
        EnsureNewsStyles();
        var close = CloseSpot(r);          // the very spot of What's new's ×
        GUI.Label(close, "×", newsClose);
        cardCloseRect = new Rect(close.x - 6, close.y - 6, close.width + 12, close.height + 12);
        GUI.Label(new Rect(r.x + pad, y, tw - 24, Overlay.Layout.HeaderHeight * u), Fit(s.Title, cardHead, tw - 24), cardHead);
        y += Overlay.Layout.FirstLine * u;
        GUI.Label(new Rect(r.x + pad, y, tw, lineH), Fit(s.ReadyLine, cardText, tw), cardText);
        y += lineH;
        cardSmall.normal.textColor = amber ? CardAmber : CardGreen;
        GUI.Label(new Rect(r.x + pad, y, tw, lineH), Fit(s.SummaryLine(alert), cardSmall, tw), cardSmall);
        y += lineH;
        DrawLastRow(r, y, pad, u, lineH, tw);
        GUI.color = was;
    }

    /// <summary>The What's new card's height for <paramref name="count"/> mods at <paramref name="width"/>.</summary>
    private float NewsHeight(float width, int count) => NewsLayout(new Rect(0, 0, width, 0), count, false, 1f);

    private void DrawNewsContent(Rect r, int count, float alpha)
    {
        if (alpha <= 0.01f)
        {
            newsCloseRect = Rect.zero;
            return;
        }
        NewsLayout(r, count, true, alpha);
    }

    /// <summary>Lays out (and with <paramref name="draw"/> draws) the What's new card: title and ×, one line per mod, the foot.</summary>
    private float NewsLayout(Rect r, int count, bool draw, float alpha)
    {
        EnsureNewsStyles();
        const float padX = 10, padY = 9;
        float inner = r.width - 2 * padX, y = r.y;
        UnityEngine.Color was = GUI.color;
        if (draw)
            GUI.color = new UnityEngine.Color(1, 1, 1, alpha);
        EnsureOneLineStyles();
        var title = new GUIContent(Fit("WHAT'S NEW  ·  R.A.T.S. KEEL", oneTitle, inner - 26));
        const float th = 24;
        if (draw)
        {
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(new Rect(r.x, y, r.width, th), headerBack);
            GUI.Label(new Rect(r.x + padX, y, inner - 26, th), title, oneTitle);
            var close = CloseSpot(r);
            GUI.Label(close, "×", newsClose);
            newsCloseRect = new Rect(close.x - 6, close.y - 6, close.width + 12, close.height + 12);
        }
        y += th + 8;
        foreach (NewsItem n in news.Take(count))
        {
            float tx = r.x + padX + 84, tw = inner - 84;
            string ver = n.VersionText;
            // name and version on one line: the version goes first when it does not fit, then the name is cut
            string head = ver.Length > 0 && oneName.CalcSize(new GUIContent($"{n.Name}  {ver}")).x <= tw
                ? $"{n.Name}  <color=#{KeelPalette.Muted:X6}>{ver}</color>"
                : Fit(n.Name, oneName, tw);
            var headC = new GUIContent(head);
            float hh = oneName.CalcHeight(headC, tw);
            string line = Fit(n.Lines.FirstOrDefault() ?? "", oneLine, tw);
            var lineC = new GUIContent(line);
            float lh = line.Length > 0 ? oneLine.CalcHeight(lineC, tw) : 0f;
            if (draw)
            {
                GUI.Label(new Rect(r.x + padX, y + 2, 74, 18), n.Tag, newsPills[n.Kind]);
                GUI.Label(new Rect(tx, y, tw, hh), headC, oneName);
                if (lh > 0)
                    GUI.Label(new Rect(tx, y + hh, tw, lh), lineC, oneLine);
            }
            y += Mathf.Max(22f, hh + lh) + 6;
        }
        int more = news.Count - count;
        var moreLink = new GUIContent("(Read More)");
        float linkWidth = newsLink.CalcSize(moreLink).x;
        string summary = count == 0 ? $"{news.Count} {(news.Count == 1 ? "mod" : "mods")} changed"
                         : more > 0 ? $"+{more} more" : "";
        var foot = new GUIContent(Fit(summary, oneFoot, inner - linkWidth - 4));
        float fh = newsLink.CalcHeight(moreLink, linkWidth);
        if (draw)
        {
            float x = r.x + padX;
            if (foot.text.Length > 0)
            {
                float width = oneFoot.CalcSize(foot).x;
                GUI.Label(new Rect(x, y, width, fh), foot, oneFoot);
                x += width + 4;
            }
            GUI.Label(new Rect(x, y, linkWidth, fh), moreLink, newsLink);
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(new Rect(x, y + fh - 2, linkWidth, 1), Tint(Rgb(KeelPalette.Accent)));
        }
        y += fh + padY;
        GUI.color = was;
        return y - r.y;
    }

    private GUIStyle oneTitle, oneName, oneLine, oneFoot, newsLink;

    /// <summary>Copies of the card's text styles that never wrap: every line is one line, cut to fit.</summary>
    private void EnsureOneLineStyles()
    {
        if (oneTitle != null)
            return;
        GUIStyle One(GUIStyle s) => new GUIStyle(s) { wordWrap = false, clipping = TextClipping.Clip };
        oneTitle = One(newsTitle);
        oneTitle.alignment = TextAnchor.MiddleLeft;
        oneName = One(newsName);
        oneLine = One(newsLine);
        oneFoot = One(newsFoot);
        newsLink = One(Text(newsFoot.fontSize, KeelPalette.Accent));
    }

    /// <summary><paramref name="text"/> cut (with ...) to what fits <paramref name="width"/> in <paramref name="style"/> on one line.</summary>
    private static string Fit(string text, GUIStyle style, float width)
    {
        if (string.IsNullOrEmpty(text) || style.CalcSize(new GUIContent(text)).x <= width)
            return text;
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (style.CalcSize(new GUIContent(text.Substring(0, mid).TrimEnd() + "…")).x <= width) lo = mid; else hi = mid - 1;
        }
        return text.Substring(0, lo).TrimEnd() + "…";
    }

    // ---- clicks ------------------------------------------------------------------------------------------------------------------

    private void HandleCardEvents()
    {
        Event e = Event.current;
        if (e.type != EventType.MouseDown)
            return;
        if (NewsClick(e.mousePosition, e.button))
        {
            e.Use();
            return;
        }
        if (CardClick(e.mousePosition, e.button))
            e.Use();
    }

    /// <summary>
    /// The card's last line: the credit (a click opens his Steam profile), or, while a mod waits for a Workshop item it needs,
    /// that item's name and a "Get it" button that opens its page.
    /// </summary>
    private void DrawLastRow(Rect r, float y, float pad, float u, float lineH, float tw)
    {
        getItRect = Rect.zero;
        getItItem = null;
        List<(RequiredItem item, int mods)> missing = RequiredItems.Missing(Keel.Plugins);
        if (missing.Count == 0)
        {
            creditRect = new Rect(r.x + pad, y, Overlay.Layout.CreditWidth * u, lineH);
            GUI.Label(creditRect, Overlay.Layout.Credit, cardCredit);      // quiet; a click opens his Steam profile
            return;
        }
        EnsureStyles();
        creditRect = Rect.zero;
        RequiredItem item = missing[0].item;
        float button = 64f * u;
        getItRect = new Rect(r.x + r.width - pad - button, y, button, lineH);
        getItItem = item;
        cardSmall.normal.textColor = CardAmber;
        // The card is narrow: only the item's name stands here (a longer sentence was cut off in the VM, 2026-11-21). The report's rows
        // say which mods wait for it, and how many is in the card's problem count above.
        GUI.Label(new Rect(r.x + pad, y, tw - button - 6f, lineH), Fit(item.Name, cardSmall, tw - button - 6f), cardSmall);
        GUI.Label(getItRect, "Get it", buttonStyle);
    }

    /// <summary>A click on the Keel card: its × collapses it to a report button (whose own × closes it), the credit opens his Steam profile, anywhere else opens the report.
    /// Only the left button counts (no right click, his word 2026-11-12).</summary>
    private bool CardClick(Vector2 gui, int button)
    {
        if (!CardVisible || !cardRect.Contains(gui))
            return false;
        if (button != 0)
            return false;
        if (!FreshClick())
            return true;
        if (summaryCollapsed)
        {
            if (cardCloseRect.Contains(gui))
                cardHidden = true;          // gone for this game; Ctrl+Shift+K still opens the report
            else
                SetReportOpen(true);
            return true;
        }
        if (cardCloseRect.Contains(gui))
        {
            summaryCollapsed = true;
            return true;
        }
        if (getItItem != null && getItRect.Contains(gui))
        {
            OpenItem(getItItem);
            return true;
        }
        if (creditRect.Contains(gui))
        {
            try { Application.OpenURL(Overlay.Layout.ProfileUrl); } catch (Exception) { /* no browser: nothing to do */ }
            return true;
        }
        SetReportOpen(true);
        return true;
    }

    // ---- the main menu's EXIT and panel --------------------------------------------------------------------------------------

    // Shares of the game's UI root, measured on the globe build at 1080p and 4K.
    private const float MenuColumnShare = 0.2f;           // the menu's entries sit in the left fifth
    private const float LowestEntryShare = 0.9f;          // below that: the version line, not an entry
    private const float PanelMinShare = 0.1f, PanelMaxShare = 0.4f;   // the menu panel's right edge; anything else is not it
    private const float ExitFallbackShare = 0.56f;        // EXIT ends near 0.50 at 1080p with ALTERNATIVE MODS in the menu
    private const float PanelFallbackShare = 0.1875f;     // 360 of 1920
    private const int MaxVisuals = 4000;                  // a cap on the walk through the menu's visual tree

    private float exitBottom = -1f, menuMeasuredAt = -10f, panelRight = -1f, menuMeasuredH = -1f;
    private int menuHits, menuTries;
    private long menuSceneTicks = -1;
    private Type mainMenuType;

    /// <summary>The right edge of the grey menu panel in GUI units, as <see cref="MeasureMenu"/> found it.</summary>
    private float PanelRight(float screenW) => panelRight > 0f ? panelRight * screenW : screenW * PanelFallbackShare;

    /// <summary>
    /// The lower edge of the main menu's lowest entry (EXIT) in GUI units into <see cref="exitBottom"/>, and the menu panel's right
    /// edge into <see cref="panelRight"/>: the lowest Button of the game's MainMenuView in the menu column (Noesis, by name),
    /// measured on a size change or a new scene, then now and then (not every frame or second). Else the fallback shares.
    /// </summary>
    private void MeasureMenu(float screenH)
    {
        float now = Time.realtimeSinceStartup, px = screenH * guiScale;
        // Once a second hitched the game (~90 ms each time, the by-name type lookups, now made once). The menu moves when the screen
        // changes size or a scene is loaded (a Mods-menu reload builds it again): then measure, once more 3 s after the first hit (it
        // may still be settling), retry a few times if nothing is found, and after that every 10 s, for an entry a mod adds later.
        if (!Mathf.Approximately(menuMeasuredH, px) || menuSceneTicks != LoadingWindow.SceneLoadedTicks)
        {
            menuMeasuredH = px;
            menuSceneTicks = LoadingWindow.SceneLoadedTicks;
            menuHits = menuTries = 0;
            menuMeasuredAt = -10f;
        }
        bool settled = menuHits >= 2 || menuTries >= 5;
        if (now - menuMeasuredAt < (settled ? 10f : menuHits == 0 ? 1f : 3f))
            return;
        menuMeasuredAt = now;
        menuTries++;
        float found = -1f;
        try
        {
            found = LowestEntry() * screenH;
        }
        catch (Exception)
        {
            // the fallback below
        }
        if (found > 0f)
            menuHits++;
        if (found > 0f || menuHits == 0)
            exitBottom = found > 0f ? found : screenH * ExitFallbackShare;
    }

    /// <summary>Noesis' visual tree helpers, found by name (Keel references no game or Noesis assembly).</summary>
    private sealed class VisualTree
    {
        public Type Point;
        public MethodInfo Count, Child;
        public object Root;
        private static Type treeType, pointType;     // AccessTools.TypeByName walks every assembly on each call: look up once

        public static VisualTree Of(object view)
        {
            Type tree = treeType ??= AccessTools.TypeByName("Noesis.VisualTreeHelper"), point = pointType ??= AccessTools.TypeByName("Noesis.Point");
            if (view == null || tree == null || point == null)
                return null;
            const BindingFlags statics = BindingFlags.Public | BindingFlags.Static;
            MethodInfo root = tree.GetMethod("GetRoot", statics);
            return new VisualTree
            {
                Point = point, Count = tree.GetMethod("GetChildrenCount", statics), Child = tree.GetMethod("GetChild", statics),
                Root = root?.Invoke(null, new[] { view }) ?? view,
            };
        }
    }

    // The lowest entry's lower edge as a share of the UI root's height (-1: none); sets panelRight on the way.
    private float LowestEntry()
    {
        mainMenuType ??= AccessTools.TypeByName("SeapowerUI.MainMenuView");
        object view = mainMenuType?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        VisualTree vt = VisualTree.Of(view);
        if (vt == null)
            return -1f;
        // The frame the shares are measured in: the UI root. In some game versions GetRoot gives a bare Visual without a size (0x0):
        // then the menu view itself is the frame (it fills the screen there).
        object frame = Prop(vt.Root, "ActualWidth") > 0 && Prop(vt.Root, "ActualHeight") > 0 ? vt.Root : view;
        (float x0, float y0) = ToScreen(frame, vt.Point, 0, 0);
        (float x1, float y1) = ToScreen(frame, vt.Point, Prop(frame, "ActualWidth"), Prop(frame, "ActualHeight"));
        if (y1 <= y0 || x1 <= x0)
            return -1f;
        (float vx, float _) = ToScreen(view, vt.Point, Prop(view, "ActualWidth"), 0);
        float pr = (vx - x0) / (x1 - x0);
        panelRight = pr > PanelMinShare && pr < PanelMaxShare ? pr : -1f;
        float best = -1f;
        var stack = new Stack<object>();
        stack.Push(view);
        int seen = 0;
        while (stack.Count > 0 && seen++ < MaxVisuals)
        {
            object el = stack.Pop();
            if (IsShownButton(el))
            {
                (float bx, float by) = ToScreen(el, vt.Point, 0, Prop(el, "ActualHeight"));
                float fx = (bx - x0) / (x1 - x0), fy = (by - y0) / (y1 - y0);
                if (fx < MenuColumnShare && fy > best && fy < LowestEntryShare)
                    best = fy;
            }
            int n = (int)vt.Count.Invoke(null, new[] { el });
            for (int i = 0; i < n; i++)
                stack.Push(vt.Child.Invoke(null, new[] { el, (object)i }));
        }
        return best;
    }

    private static bool IsShownButton(object el) =>
        el.GetType().Name.EndsWith("Button") && Prop(el, "ActualHeight") > 0 && el.GetType().GetProperty("IsVisible")?.GetValue(el) is true;

    private static float Prop(object o, string name)
    {
        object v = o.GetType().GetProperty(name)?.GetValue(o);
        return v == null ? 0f : Convert.ToSingle(v);
    }

    private static (float, float) ToScreen(object visual, Type point, float x, float y)
    {
        object p = Activator.CreateInstance(point, x, y);
        object s = visual.GetType().GetMethod("PointToScreen", new[] { point })?.Invoke(visual, new[] { p });
        if (s == null)
            return (0f, 0f);
        return (Convert.ToSingle(point.GetProperty("X")?.GetValue(s) ?? point.GetField("X")?.GetValue(s) ?? 0f),
                Convert.ToSingle(point.GetProperty("Y")?.GetValue(s) ?? point.GetField("Y")?.GetValue(s) ?? 0f));
    }

    // ---- styles and textures ---------------------------------------------------------------------------------------------------

    private readonly Dictionary<UnityEngine.Color, Texture2D> tints = new();

    private Texture2D Tint(UnityEngine.Color c)
    {
        if (!tints.TryGetValue(c, out Texture2D t))
        {
            t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            tints[c] = t;
        }
        return t;
    }

    private void EnsureCardStyles()
    {
        EnsureNewsStyles();
        circleTex ??= Disc(64);
        float u = LoadingWindow.SummaryScale / guiScale;
        if (Mathf.Abs(u - cardStyleUnit) < 0.001f)
            return;
        cardStyleUnit = u;
        GUIStyle Make(float points, FontStyle style, UnityEngine.Color color) => new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(8, Mathf.RoundToInt(points * 96f / 72f * u)), fontStyle = style, wordWrap = false, clipping = TextClipping.Clip,
            alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
            normal = { textColor = color },
        };
        cardHead = Make(10, FontStyle.Normal, CardDim);
        cardText = Make(10, FontStyle.Normal, CardText);
        cardSmall = Make(10, FontStyle.Normal, CardDim);
        cardCredit = Make(9, FontStyle.Normal, CardDim);
    }

    /// <summary>A white disc with a soft edge, tinted with GUI.color.</summary>
    internal static Texture2D Disc(int size)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        var px = new UnityEngine.Color[size * size];
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                px[y * size + x] = new UnityEngine.Color(1, 1, 1, Mathf.Clamp01(r - d));
            }
        t.SetPixels(px);
        t.Apply();
        return t;
    }
}
