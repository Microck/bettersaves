using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Celeste;
using Celeste.Mod.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.BetterSaves;

public sealed class OuiBetterSaves : Oui {
    private const float ViewWidth = 1920f;
    private const float ViewHeight = 1080f;
    private const float PanelX = 250f;
    private const float PanelY = 95f;
    private const float PanelWidth = 1420f;
    private const float PanelHeight = 890f;
    private const float ListX = 300f;
    private const float ListY = 310f;
    private const float ListWidth = 860f;
    private const float RowHeight = 82f;
    private const int VisibleRows = 5;

    private static readonly Color PanelColor = new Color(246, 236, 215) * 0.97f;
    private static readonly Color PanelShadow = new Color(16, 14, 31) * 0.44f;
    private static readonly Color Ink = new Color(36, 31, 50);
    private static readonly Color MutedInk = new Color(86, 75, 104);
    private static readonly Color SoftInk = new Color(127, 107, 129);
    private static readonly Color PaperLine = new Color(74, 58, 88) * 0.34f;
    private static readonly Color RowPaper = new Color(255, 248, 234) * 0.92f;
    private static readonly Color SelectedPaper = new Color(255, 253, 244) * 0.99f;
    private static readonly Color ContinueRed = new Color(207, 74, 93) * 0.92f;
    private static readonly Color Gold = new Color(241, 197, 78);
    private static readonly Color Outline = new Color(18, 14, 26) * 0.52f;
    private static readonly Color HelpBlue = new Color(76, 103, 153);

    private static Atlas? journalAtlas;
    private static bool journalAtlasLoadAttempted;

    private enum MenuMode {
        Deck,
        Options,
        Rename,
        Message,
        ConfirmDelete
    }

    private enum DeckFilter {
        Recent,
        Pinned,
        InProgress,
        Complete,
        Modded,
        Archived,
        All
    }

    private TextMenu? menu;
    private List<SaveDeckEntry> entries = new();
    private List<SaveDeckEntry> allEntries = new();
    private SaveDeckEntry? continueEntry;
    private SaveDeckEntry? activeEntry;
    private SaveDeckEntry? renamingEntry;
    private Action? messageBackAction;
    private string renameText = "";
    private MenuMode menuMode;
    private int deckSelection;
    private int lastSelection = -1;
    private DeckFilter activeFilter = DeckFilter.Recent;
    private float deckEase;
    private float filterEase = 1f;
    private float selectionPulse;
    private float ambientTimer;
    private float deckInputBlockTimer;
    private bool deckConfirmReleaseLock;
    private bool showHelp;
    private string filter = "";

    public override IEnumerator Enter(Oui from) {
        Visible = true;
        Focused = true;
        deckEase = 0f;
        filterEase = 1f;
        selectionPulse = 0f;
        RebuildMenu();
        yield break;
    }

    public override IEnumerator Leave(Oui next) {
        Focused = false;
        Visible = false;
        menu?.Close();
        menu = null;
        yield break;
    }

    public override void Update() {
        base.Update();

        if (!Visible) {
            return;
        }

        deckEase = Calc.Approach(deckEase, 1f, Engine.DeltaTime * 4.8f);
        filterEase = Calc.Approach(filterEase, 1f, Engine.DeltaTime * 7.5f);
        selectionPulse = Calc.Approach(selectionPulse, 0f, Engine.DeltaTime * 5.6f);
        ambientTimer += Engine.DeltaTime;
        if (deckInputBlockTimer > 0f) {
            deckInputBlockTimer = Math.Max(0f, deckInputBlockTimer - Engine.DeltaTime);
        }

        if (menuMode == MenuMode.Rename) {
            UpdateRenameInput();
            return;
        }

        if (Input.MenuCancel.Pressed || Input.ESC.Pressed || Input.Pause.Pressed) {
            HandleCancel();
            return;
        }

        if (menuMode != MenuMode.Deck) {
            return;
        }

        if (deckConfirmReleaseLock) {
            if (ConfirmInputHeld()) {
                UpdateSelectedDetail();
                return;
            }

            deckConfirmReleaseLock = false;
        }

        if (deckInputBlockTimer > 0f) {
            UpdateSelectedDetail();
            return;
        }

        if (InfoButtonPressed()) {
            ToggleHelp();
            return;
        }

        if (NewFileButtonPressed()) {
            CreateNewFile();
            return;
        }

        if (Input.MenuConfirm.Pressed || MInput.Keyboard.Pressed(Keys.Enter) || MInput.Keyboard.Pressed(Keys.C)) {
            SaveDeckEntry? selected = FindSelectedEntry();
            if (selected != null) {
                Play(selected);
            }
            return;
        }

        if (Input.MenuUp.Pressed || MInput.Keyboard.Pressed(Keys.Up) || MInput.Keyboard.Pressed(Keys.W) || MInput.Keyboard.Pressed(Keys.K)) {
            MoveDeckSelection(-1);
            return;
        }

        if (Input.MenuDown.Pressed || MInput.Keyboard.Pressed(Keys.Down) || MInput.Keyboard.Pressed(Keys.S) || MInput.Keyboard.Pressed(Keys.J)) {
            MoveDeckSelection(1);
            return;
        }

        bool pageUpPressed =
            CoreModule.Settings.MenuPageUp.Pressed ||
            MInput.Keyboard.Pressed(Keys.PageUp) ||
            MInput.Keyboard.Pressed(Keys.Home) ||
            MInput.Keyboard.Pressed(Keys.U);
        bool pageDownPressed =
            CoreModule.Settings.MenuPageDown.Pressed ||
            MInput.Keyboard.Pressed(Keys.PageDown) ||
            MInput.Keyboard.Pressed(Keys.End) ||
            MInput.Keyboard.Pressed(Keys.I);
        bool leftTriggerPressed = MInput.GamePads[Input.Gamepad].LeftTriggerPressed(0.5f);
        bool rightTriggerPressed = MInput.GamePads[Input.Gamepad].RightTriggerPressed(0.5f);
        bool leftTriggerHeld = MInput.GamePads[Input.Gamepad].LeftTriggerCheck(0.5f);
        bool rightTriggerHeld = MInput.GamePads[Input.Gamepad].RightTriggerCheck(0.5f);
        bool sortPressed =
            MInput.Keyboard.Pressed(Keys.T) ||
            (leftTriggerHeld && rightTriggerPressed) ||
            (rightTriggerHeld && leftTriggerPressed);

        if (MInput.Keyboard.Pressed(Keys.F1) || MInput.Keyboard.Pressed(Keys.OemQuestion)) {
            ToggleHelp();
            return;
        }

        if (sortPressed) {
            MoveSort();
            return;
        }

        if (Input.MenuJournal.Pressed || MInput.Keyboard.Pressed(Keys.Tab) || MInput.Keyboard.Pressed(Keys.X) || MInput.Keyboard.Pressed(Keys.O)) {
            OpenOptionsForCurrent();
            return;
        }

        if (pageUpPressed) {
            MoveSelectionPage(-1);
            if (CoreModule.Settings.MenuPageUp.Pressed) {
                CoreModule.Settings.MenuPageUp.ConsumePress();
            }
            Audio.Play(SFX.ui_main_savefile_roll_up);
            return;
        }

        if (pageDownPressed) {
            MoveSelectionPage(1);
            if (CoreModule.Settings.MenuPageDown.Pressed) {
                CoreModule.Settings.MenuPageDown.ConsumePress();
            }
            Audio.Play(SFX.ui_main_savefile_roll_down);
            return;
        }

        bool sectionBackPressed =
            leftTriggerPressed ||
            Input.MenuLeft.Pressed ||
            MInput.Keyboard.Pressed(Keys.Left) ||
            MInput.Keyboard.Pressed(Keys.Q) ||
            MInput.Keyboard.Pressed(Keys.F) ||
            MInput.Keyboard.Pressed(Keys.H) ||
            MInput.Keyboard.Pressed(Keys.OemOpenBrackets);
        bool sectionForwardPressed =
            rightTriggerPressed ||
            Input.MenuRight.Pressed ||
            MInput.Keyboard.Pressed(Keys.Right) ||
            MInput.Keyboard.Pressed(Keys.E) ||
            MInput.Keyboard.Pressed(Keys.R) ||
            MInput.Keyboard.Pressed(Keys.L) ||
            MInput.Keyboard.Pressed(Keys.OemCloseBrackets);

        if (sectionBackPressed || sectionForwardPressed) {
            MoveFilter(sectionForwardPressed ? 1 : -1);
            return;
        }

        Keys[] pressedKeys = MInput.Keyboard.CurrentState.GetPressedKeys();
        foreach (Keys key in pressedKeys) {
            if (!MInput.Keyboard.Pressed(key)) {
                continue;
            }

            if (key == Keys.Back && filter.Length > 0) {
                filter = filter.Substring(0, filter.Length - 1);
                RebuildMenu();
            } else if (IsReservedTypingKey(key)) {
                continue;
            } else if (key >= Keys.A && key <= Keys.Z) {
                filter += key.ToString().ToLowerInvariant();
                RebuildMenu();
            } else if (key >= Keys.D0 && key <= Keys.D9) {
                filter += ((int) key - (int) Keys.D0).ToString(CultureInfo.InvariantCulture);
                RebuildMenu();
            }
        }

        UpdateSelectedDetail();
    }

    private void RebuildMenu() {
        RebuildMenu(preserveSelection: true);
    }

    private void RebuildMenu(bool preserveSelection) {
        int previousSelection = preserveSelection && menuMode == MenuMode.Deck ? deckSelection : 0;
        RemoveMenu();

        allEntries = SaveDeckCatalog.LoadEntries(includeArchived: true, filter, BetterSavesModule.Settings.SortMode);
        entries = FilterEntries(allEntries);
        activeEntry = null;
        renamingEntry = null;
        messageBackAction = null;
        menuMode = MenuMode.Deck;
        lastSelection = -1;

        continueEntry = allEntries.Find(entry => !entry.IsArchived);
        deckSelection = Calc.Clamp(previousSelection, 0, Math.Max(0, DeckItemCount - 1));
        menu = new TextMenu {
            Focused = false,
            Visible = false,
            AutoScroll = false,
            MinWidth = 1f
        };
        menu.OnESC = () => Overworld.Goto<OuiMainMenu>();
        menu.OnCancel = () => Overworld.Goto<OuiMainMenu>();
        Scene.Add(menu);
        UpdateSelectedDetail(force: true);
    }

    public override void Render() {
        base.Render();

        if (!Visible || menuMode != MenuMode.Deck) {
            return;
        }

        RenderDeck();
    }

    private void RenderDeck() {
        SaveDeckEntry? selected = FindSelectedEntry();
        bool continueSelected = continueEntry != null && deckSelection == 0;

        Draw.Rect(PanelX + 18f, PanelY + 22f, PanelWidth, PanelHeight, PanelShadow);
        DrawPaperPanel(PanelX, PanelY, PanelWidth, PanelHeight);
        DrawAmbientSnow();

        DrawPaperTape(720f, 108f, -0.18f);
        DrawTitle();
        DrawFilterBanner();
        DrawContinue(continueSelected);
        DrawSaveRows(selected, continueSelected);
        DrawDetailPanel(selected ?? (entries.Count > 0 ? entries[0] : null));
        DrawInfoButton();
        if (showHelp) {
            DrawHelpPanel();
        }
    }

    private static void DrawPaperPanel(float x, float y, float width, float height) {
        Draw.Rect(x, y, width, height, PanelColor);
        Draw.HollowRect(x, y, width, height, new Color(255, 255, 255) * 0.7f);
        Draw.HollowRect(x + 3f, y + 3f, width - 6f, height - 6f, PaperLine);

        // Small clipped corners keep the flat rectangles from reading like a desktop modal.
        Color corner = Color.Transparent;
        Draw.Rect(x, y, 18f, 18f, corner);
        Draw.Rect(x + width - 18f, y, 18f, 18f, corner);
        Draw.Rect(x, y + height - 18f, 18f, 18f, corner);
        Draw.Rect(x + width - 18f, y + height - 18f, 18f, 18f, corner);
        Draw.Rect(x + 24f, y + 24f, width - 48f, 1f, new Color(255, 255, 255) * 0.62f);
        Draw.Rect(x + 24f, y + height - 26f, width - 48f, 1f, new Color(146, 116, 130) * 0.18f);
    }

    private void DrawTitle() {
        DrawText("SAVE FILES", new Vector2(315f, 132f), 0.84f, Ink, Vector2.Zero, 0.48f);
        DrawText(SaveCountLabel(allEntries.Count), new Vector2(1608f, 154f), 0.48f, Ink, new Vector2(1f, 0f), 0.16f);
    }

    private void DrawFilterBanner() {
        string label = FilterLabel(activeFilter) + " Files";
        int count = FilterEntries(allEntries).Count;
        float y = 256f - (1f - Ease.CubeOut(filterEase)) * 12f;
        float flash = 1f - filterEase;
        Color ribbon = new Color(78, 60, 101);

        Draw.Rect(ListX, y + 44f, ListWidth, 2f, PaperLine);
        Draw.Rect(ListX, y + 47f, 120f + flash * 70f, 4f, ContinueRed * (0.42f + flash * 0.24f));
        DrawText(label, new Vector2(ListX + 2f, y), 0.5f + flash * 0.03f, ribbon, Vector2.Zero, 0.2f);
        DrawText(SaveCountLabel(count), new Vector2(ListX + 315f, y + 8f), 0.34f, MutedInk);
        DrawText(SortLabel(BetterSavesModule.Settings.SortMode), new Vector2(ListX + 535f, y + 9f), 0.3f, SoftInk);
        DrawFilterDots(ListX + ListWidth - 150f, y + 19f);
    }

    private void DrawFilterDots(float x, float y) {
        int filterCount = Enum.GetValues(typeof(DeckFilter)).Length;
        for (int index = 0; index < filterCount; index++) {
            bool active = index == (int) activeFilter;
            float width = active ? 28f : 10f;
            Color color = active ? ContinueRed * 0.72f : SoftInk * 0.46f;
            Draw.Rect(x, y, width, 4f, color);
            x += width + 8f;
        }
    }

    private void DrawContinue(bool continueSelected) {
        if (continueEntry == null) {
            Draw.Rect(ListX, ListY, ListWidth, 78f, new Color(255, 248, 229) * 0.62f);
            Draw.HollowRect(ListX, ListY, ListWidth, 78f, PaperLine);
            string emptyLabel = allEntries.Count == 0 ? "No save files found" : "No saves in " + FilterLabel(activeFilter);
            DrawText(emptyLabel, new Vector2(ListX + 35f, ListY + 20f), 0.55f, Ink);
            return;
        }

        SaveDeckEntry entry = continueEntry;
        Color fill = continueSelected ? SelectedPaper : new Color(252, 246, 237) * 0.9f;
        DrawPaperStrip(ListX, ListY, ListWidth, 86f, fill, continueSelected);
        DrawRowStateMark(new Vector2(ListX + 29f, ListY + 43f), entry, continueSelected);
        DrawText(FitText(entry.Name, 360f, 0.5f), new Vector2(ListX + 54f, ListY + 11f), 0.5f, Ink, Vector2.Zero, continueSelected ? 0.14f : 0.04f);
        DrawText(FitText(RowMetaLabel(entry), 430f, 0.31f), new Vector2(ListX + 56f, ListY + 52f), 0.31f, MutedInk);
        float statY = ListY + 30f;
        DrawSaveStatIcons(ListX + 390f, statY, entry, continueSelected);
        float timeX = ListX + ListWidth - 214f;
        DrawTimeWithIcon(entry.FormattedTime, timeX, statY - 1f, 0.38f, Ink);
        DrawText(entry.RelativeAge, new Vector2(timeX + 40f, statY + 27f), 0.31f, MutedInk);
        float pulse = continueSelected ? 1f + (float) Math.Sin(Engine.Scene.TimeActive * 5f) * 0.04f : 1f;
        DrawContinueArrow(new Vector2(ListX + ListWidth - 34f, ListY + 42f), continueSelected ? ContinueRed : Ink, continueSelected ? 1.12f * pulse : 1f);
    }

    private void DrawSaveRows(SaveDeckEntry? selected, bool continueSelected) {
        if (entries.Count == 0) {
            return;
        }

        int selectedIndex = SelectedEntryIndex(selected);
        int first = Calc.Clamp(selectedIndex - 2, 0, Math.Max(0, entries.Count - VisibleRows));
        int last = Math.Min(entries.Count, first + VisibleRows);
        float y = ListY + 124f;

        for (int index = first; index < last; index++) {
            SaveDeckEntry entry = entries[index];
            bool selectedRow = !continueSelected && selected?.Slot == entry.Slot;
            int visibleIndex = index - first;
            float rowEase = Ease.CubeOut(Calc.Clamp(deckEase * 1.35f - visibleIndex * 0.08f, 0f, 1f));
            float rowY = y + (1f - rowEase) * 18f;
            DrawSaveRow(entry, index, rowY, selectedRow, rowEase, selectionPulse, ambientTimer);
            y += RowHeight + 10f;
        }

        if (entries.Count > VisibleRows) {
            DrawScrollHint(first, entries.Count);
        }
    }

    private static void DrawSaveRow(SaveDeckEntry entry, int index, float y, bool selected, float rowEase, float pulse, float timer) {
        Color fill = selected ? SelectedPaper : RowPaper;
        Color text = Ink;
        Color muted = MutedInk;
        float breathe = selected ? (float) Math.Sin(timer * 4f) * 1.2f : 0f;
        float selectedLift = selected ? pulse * 1.5f + breathe : 0f;
        float contentY = y - selectedLift;
        DrawPaperStrip(ListX, contentY, ListWidth, RowHeight, fill, selected);

        DrawRowStateMark(new Vector2(ListX + 29f, contentY + RowHeight / 2f), entry, selected);
        DrawText(FitText(entry.Name, 360f, 0.48f), new Vector2(ListX + 54f, contentY + 8f), 0.48f, text, Vector2.Zero, selected ? 0.12f : 0f);
        DrawText(FitText(RowMetaLabel(entry), 430f, 0.31f), new Vector2(ListX + 56f, contentY + 48f), 0.31f, muted);
        float statY = contentY + 27f;
        DrawSaveStatIcons(ListX + 424f, statY, entry, selected);
        DrawTimeWithIcon(entry.FormattedTime, ListX + 610f, statY - 1f, 0.35f, text);
        DrawText(entry.RelativeAge, new Vector2(ListX + ListWidth - 28f, contentY + 19f), 0.34f, selected ? ContinueRed : new Color(139, 62, 58), new Vector2(1f, 0f), selected ? 0.08f : 0f);
    }

    private static void DrawRowStateMark(Vector2 center, SaveDeckEntry entry, bool selected) {
        Color color = entry.IsPinned ? Gold : entry.IsArchived ? SoftInk : selected ? ContinueRed : PaperLine;
        Draw.Rect(center.X - 6f, center.Y - 6f, 12f, 12f, color * (selected ? 0.92f : 0.64f));
        Draw.HollowRect(center.X - 9f, center.Y - 9f, 18f, 18f, selected ? ContinueRed * 0.82f : new Color(255, 255, 255) * 0.42f);
    }

    private static void DrawSaveStatIcons(float x, float y, SaveDeckEntry entry, bool selected) {
        MTexture? berry = TryTexture(GFX.Gui, "collectables/strawberry", "strawberry");
        Color text = selected ? Ink : MutedInk;
        if (berry != null) {
            DrawTextureFit(berry, x, y - 7f, 34f, 34f, Color.White);
            DrawText("x" + entry.TotalStrawberries.ToString(CultureInfo.InvariantCulture), new Vector2(x + 39f, y - 1f), 0.31f, text);
        } else {
            DrawText(entry.TotalStrawberries.ToString(CultureInfo.InvariantCulture) + " berries", new Vector2(x, y), 0.34f, text);
        }

        if (entry.HasBackup) {
            MTexture? cassette = TryTexture(GFX.Gui, "collectables/cassette", "cassette");
            if (cassette != null) {
                DrawTextureFit(cassette, x + 105f, y - 6f, 32f, 32f, Color.White);
            }
        }
    }

    private void DrawAmbientSnow() {
        Color snow = new Color(255, 255, 255) * 0.14f;
        for (int index = 0; index < 18; index++) {
            float seed = index * 67.13f;
            float x = PanelX + 56f + (seed * 17f % (PanelWidth - 112f));
            float y = PanelY + 48f + ((seed * 31f + ambientTimer * (12f + index % 5)) % (PanelHeight - 96f));
            float drift = (float) Math.Sin(ambientTimer * 0.7f + index) * 5f;
            float size = index % 3 == 0 ? 3f : 2f;
            Draw.Rect(x + drift, y, size, size, snow);
        }
    }

    private static void DrawContinueArrow(Vector2 center, Color color, float scale) {
        MTexture? arrow = TryTexture(GFX.Gui, "tinyarrow", "dotarrow");
        if (arrow != null) {
            float width = 38f * scale;
            float height = 38f * scale;
            DrawTextureFit(arrow, center.X - width / 2f, center.Y - height / 2f, width, height, color);
            return;
        }

        DrawFilledArrow(center, Outline, scale * 1.12f);
        DrawFilledArrow(center, color, scale);
    }

    private static void DrawFilledArrow(Vector2 center, Color color, float scale) {
        float shaftX = center.X - 18f * scale;
        float shaftY = center.Y - 3f * scale;
        Draw.Rect(shaftX, shaftY, 21f * scale, 6f * scale, color);

        float tipX = center.X + 18f * scale;
        float baseX = center.X + 1f * scale;
        float halfHeight = 13f * scale;
        float step = Math.Max(1f, 2f * scale);
        for (float offset = -halfHeight; offset <= halfHeight; offset += step) {
            float progress = Math.Abs(offset) / halfHeight;
            float xStart = MathHelper.Lerp(baseX, tipX, progress);
            Draw.Line(
                new Vector2(xStart, center.Y + offset),
                new Vector2(tipX, center.Y + offset),
                color,
                step + 0.75f * scale);
        }
    }

    private static void DrawTimeWithIcon(string value, float x, float y, float scale, Color color) {
        DrawTimeIcon(new Vector2(x + 16f, y + 11f), LightIconColor(color), 1.34f);
        DrawText(value, new Vector2(x + 40f, y), scale, color);
    }

    private static Color LightIconColor(Color color) =>
        Color.Lerp(color, new Color(255, 255, 255), 0.76f);

    private static void DrawTimeIcon(Vector2 center, Color color, float scale) {
        MTexture? native = TimeIconTexture();
        if (native != null) {
            float size = 24f * scale;
            DrawTextureFit(native, center.X - size / 2f, center.Y - size / 2f, size, size, Color.White * 0.48f);
            return;
        }

        DrawClockIcon(center, color, scale);
    }

    private static void DrawClockIcon(Vector2 center, Color color, float scale) {
        float radius = 11f * scale;
        Draw.HollowRect(center.X - radius, center.Y - radius, radius * 2f, radius * 2f, color * 0.74f);
        Draw.Line(center, center + new Vector2(0f, -7f) * scale, color, 2.2f * scale);
        Draw.Line(center, center + new Vector2(6f, 3f) * scale, color, 2.2f * scale);
    }

    private static void DrawDeathIcon(Vector2 center, float scale) {
        MTexture? skull = TryTexture(GFX.Gui, "collectables/skullBlue", "collectables/skullRed", "collectables/skullGold");
        if (skull != null) {
            float size = 32f * scale;
            DrawTextureFit(skull, center.X - size / 2f, center.Y - size / 2f, size, size, Color.White);
            return;
        }

        Draw.Rect(center.X - 9f * scale, center.Y - 8f * scale, 18f * scale, 14f * scale, SoftInk * 0.72f);
        Draw.Rect(center.X - 5f * scale, center.Y + 6f * scale, 10f * scale, 6f * scale, SoftInk * 0.72f);
        Draw.Rect(center.X - 5f * scale, center.Y - 3f * scale, 3f * scale, 3f * scale, PanelColor);
        Draw.Rect(center.X + 2f * scale, center.Y - 3f * scale, 3f * scale, 3f * scale, PanelColor);
    }

    private void DrawInfoButton() {
        Rectangle bounds = InfoButtonBounds();
        float x = bounds.X;
        float y = bounds.Y;
        Color fill = showHelp ? HelpBlue * 0.82f : new Color(255, 248, 231) * 0.72f;
        Color text = showHelp ? Color.White : MutedInk;
        Draw.Rect(x + 4f, y + 4f, 44f, 44f, new Color(42, 32, 58) * 0.16f);
        Draw.Rect(x, y, 44f, 44f, fill);
        Draw.HollowRect(x, y, 44f, 44f, showHelp ? Color.White * 0.42f : PaperLine);
        DrawText("?", new Vector2(x + 22f, y + 21f), 0.52f, text, new Vector2(0.5f, 0.5f), showHelp ? 0f : 0.08f);
    }

    private void DrawHelpPanel() {
        float x = PanelX + PanelWidth - 540f;
        float y = PanelY + PanelHeight - 312f;
        float width = 470f;
        float height = 226f;
        float centerX = x + width / 2f;
        float contentY = y + height / 2f - 88f;
        DrawPaperStrip(x, y, width, height, new Color(255, 248, 231) * 0.95f, false);
        DrawText("Controls", new Vector2(centerX, contentY), 0.36f, Ink, new Vector2(0.5f, 0f), 0.1f);
        DrawText("Move: Up/Down, J/K", new Vector2(centerX, contentY + 40f), 0.25f, MutedInk, new Vector2(0.5f, 0f));
        DrawText("Page: PgUp/PgDn, U/I", new Vector2(centerX, contentY + 66f), 0.25f, MutedInk, new Vector2(0.5f, 0f));
        DrawText("Section: Left/Right, Q/E", new Vector2(centerX, contentY + 92f), 0.25f, MutedInk, new Vector2(0.5f, 0f));
        DrawText("Options: O, Journal", new Vector2(centerX, contentY + 118f), 0.25f, MutedInk, new Vector2(0.5f, 0f));
        DrawText("New Save: M, +", new Vector2(centerX, contentY + 144f), 0.25f, MutedInk, new Vector2(0.5f, 0f));
        DrawText("Sort: T   Help: F1, ?", new Vector2(centerX, contentY + 170f), 0.25f, MutedInk, new Vector2(0.5f, 0f));
    }

    private static bool InfoButtonPressed() {
        if (!MInput.Mouse.PressedLeftButton) {
            return false;
        }

        Vector2 rawMouse = new Vector2(MInput.Mouse.X, MInput.Mouse.Y);
        if (ContainsInfoButton(rawMouse)) {
            return true;
        }

        if (Engine.Width <= 0 || Engine.Height <= 0) {
            return false;
        }

        // Mouse coordinates can arrive in window pixels while this UI is drawn in Celeste's virtual
        // 1920x1080 space. Accept both spaces so the visible button remains clickable after scaling.
        Vector2 scaledMouse = new Vector2(rawMouse.X * ViewWidth / Engine.Width, rawMouse.Y * ViewHeight / Engine.Height);
        return ContainsInfoButton(scaledMouse);
    }

    private static Rectangle InfoButtonBounds() =>
        new((int) (PanelX + PanelWidth - 82f), (int) (PanelY + PanelHeight - 72f), 44, 44);

    private static bool ContainsInfoButton(Vector2 point) {
        Rectangle bounds = InfoButtonBounds();
        return point.X >= bounds.X &&
            point.X <= bounds.X + bounds.Width &&
            point.Y >= bounds.Y &&
            point.Y <= bounds.Y + bounds.Height;
    }

    private static void DrawPaperStrip(float x, float y, float width, float height, Color fill, bool selected) {
        Draw.Rect(x + 5f, y + 5f, width, height, new Color(40, 34, 58) * 0.14f);

        MTexture? card = TryTexture(GFX.Gui, "card");
        if (card != null) {
            DrawTextureFit(card, x, y, width, height, selected ? Color.White : new Color(255, 250, 246) * 0.9f);
        } else {
            Draw.Rect(x, y, width, height, fill);
        }

        Draw.Rect(x, y, width, height, fill * 0.78f);
        Draw.HollowRect(x, y, width, height, selected ? ContinueRed * 0.82f : PaperLine);
        Draw.Rect(x + 18f, y + 6f, width - 36f, 1f, new Color(255, 255, 255) * 0.46f);
        Draw.Rect(x + 18f, y + height - 7f, width - 36f, 1f, new Color(112, 91, 105) * 0.16f);

        if (selected) {
            Draw.HollowRect(x - 3f, y - 3f, width + 6f, height + 6f, ContinueRed * 0.56f);
            Draw.HollowRect(x + 3f, y + 3f, width - 6f, height - 6f, new Color(255, 255, 255) * 0.54f);
            Draw.Rect(x + 20f, y + 5f, width - 40f, 2f, new Color(255, 255, 255) * 0.55f);
        }
    }

    private static void DrawPaperTape(float x, float y, float rotation) {
        MTexture? tape = TryTexture(GFX.Gui, "textboxbutton");
        if (tape != null) {
            tape.DrawCentered(new Vector2(x, y), new Color(213, 238, 245) * 0.8f, 0.72f, rotation);
            return;
        }

        Draw.Rect(x - 30f, y - 10f, 60f, 20f, new Color(204, 232, 237) * 0.82f);
        Draw.HollowRect(x - 30f, y - 10f, 60f, 20f, new Color(255, 255, 255) * 0.4f);
    }

    private void DrawDetailPanel(SaveDeckEntry? selected) {
        float x = 1260f;
        float y = 310f;
        float width = 350f;
        float height = 520f;
        DrawPaperStrip(x, y, width, height, new Color(255, 248, 231) * 0.8f, false);

        DrawText("SELECTED SAVE", new Vector2(x + width / 2f, y + 33f), 0.34f, MutedInk, new Vector2(0.5f, 0f), 0.08f);

        if (selected == null) {
            DrawMapPreview(x + 35f, y + 86f, width - 70f, 154f, null);
            DrawText("No save selected", new Vector2(x + 45f, y + 285f), 0.42f, Ink);
            return;
        }

        DrawMapPreview(x + 35f, y + 86f, width - 70f, 154f, selected);
        DrawText(FitText(selected.Name, width - 86f, 0.5f), new Vector2(x + 45f, y + 272f), 0.5f, Ink, Vector2.Zero, 0.12f);
        DrawText(FitText(RowMetaLabel(selected), width - 90f, 0.31f), new Vector2(x + 45f, y + 323f), 0.31f, MutedInk);
        DrawDetailLine("Time", selected.FormattedTime, x + 45f, y + 366f, icon: true);
        DrawDetailLine("Deaths", selected.TotalDeaths.ToString(CultureInfo.InvariantCulture), x + 45f, y + 404f, DetailIcon.Deaths);
        DrawSaveStatIcons(x + 45f, y + 446f, selected, false);
        DrawText(selected.RelativeAge, new Vector2(x + 45f, y + 484f), 0.29f, Ink);
    }

    private static void DrawMapPreview(float x, float y, float width, float height, SaveDeckEntry? selected) {
        Draw.Rect(x + 4f, y + 4f, width, height, new Color(42, 32, 58) * 0.18f);
        Draw.Rect(x, y, width, height, new Color(42, 38, 61) * 0.9f);

        MTexture? back = selected == null ? null : MapPreviewTexture(selected, backLayer: true);
        MTexture? front = selected == null ? null : MapPreviewTexture(selected, backLayer: false);
        if (back != null || front != null) {
            if (back != null) {
                DrawTextureFit(back, x + 8f, y + 6f, width - 16f, height - 36f, Color.White * 0.96f);
            }

            if (front != null) {
                DrawTextureFit(front, x + 18f, y + 8f, width - 36f, height - 42f, Color.White);
            }
        } else {
            Color sky = selected?.IsModded == true ? new Color(119, 91, 151) : new Color(79, 120, 169);
            Color dusk = selected?.IsComplete == true ? new Color(206, 135, 139) : new Color(133, 104, 164);
            Draw.Rect(x, y, width, height, sky * 0.76f);
            Draw.Rect(x, y + height * 0.45f, width, height * 0.55f, dusk * 0.62f);
            Draw.Line(new Vector2(x + 18f, y + height - 24f), new Vector2(x + width * 0.42f, y + 34f), new Color(246, 239, 235) * 0.8f, 5f);
            Draw.Line(new Vector2(x + width * 0.42f, y + 34f), new Vector2(x + width - 26f, y + height - 24f), new Color(227, 221, 234) * 0.72f, 5f);
        }

        Draw.Rect(x, y + height - 28f, width, 28f, new Color(26, 35, 64) * 0.44f);
        Draw.HollowRect(x, y, width, height, new Color(255, 255, 255) * 0.34f);
        if (selected != null) {
            DrawText(FitText(selected.MapName, width - 26f, 0.26f), new Vector2(x + width / 2f, y + height - 24f), 0.26f, new Color(255, 252, 246), new Vector2(0.5f, 0f), 0.1f);
        }
    }

    private enum DetailIcon {
        None,
        Time,
        Deaths
    }

    private static void DrawDetailLine(string label, string value, float x, float y, bool icon = false) =>
        DrawDetailLine(label, value, x, y, icon ? DetailIcon.Time : DetailIcon.None);

    private static void DrawDetailLine(string label, string value, float x, float y, DetailIcon icon) {
        DrawText(label, new Vector2(x, y + 3f), 0.25f, SoftInk);
        if (icon == DetailIcon.Time) {
            DrawTimeIcon(new Vector2(x + 116f, y + 11f), LightIconColor(Ink), 1.34f);
            DrawText(value, new Vector2(x + 156f, y), 0.34f, Ink);
        } else if (icon == DetailIcon.Deaths) {
            DrawDeathIcon(new Vector2(x + 116f, y + 11f), 1f);
            DrawText(value, new Vector2(x + 156f, y), 0.34f, Ink);
        } else {
            DrawText(value, new Vector2(x + 118f, y), 0.34f, Ink);
        }
    }

    private static void DrawScrollHint(int first, int total) {
        float trackX = ListX + ListWidth + 22f;
        float trackY = ListY + 125f;
        float trackHeight = VisibleRows * (RowHeight + 10f) - 10f;
        float thumbHeight = Math.Max(58f, trackHeight * VisibleRows / total);
        float thumbY = trackY + (trackHeight - thumbHeight) * first / Math.Max(1, total - VisibleRows);
        Draw.Rect(trackX, trackY, 8f, trackHeight, new Color(140, 120, 110) * 0.28f);
        Draw.Rect(trackX - 2f, thumbY, 12f, thumbHeight, new Color(112, 92, 102) * 0.55f);
    }

    private int SelectedEntryIndex(SaveDeckEntry? selected) {
        if (selected == null) {
            return 0;
        }

        for (int index = 0; index < entries.Count; index++) {
            if (entries[index].Slot == selected.Slot) {
                return index;
            }
        }

        return 0;
    }

    private static string FitText(string text, float maxWidth, float scale) {
        if (ActiveFont.Measure(text).X * scale <= maxWidth) {
            return text;
        }

        const string ellipsis = "...";
        string trimmed = text;
        while (trimmed.Length > 0 && ActiveFont.Measure(trimmed + ellipsis).X * scale > maxWidth) {
            trimmed = trimmed.Substring(0, trimmed.Length - 1);
        }

        return trimmed.Length == 0 ? ellipsis : trimmed + ellipsis;
    }

    private static string SaveCountLabel(int count) =>
        count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " save" : " saves");

    private static string RowMetaLabel(SaveDeckEntry entry) {
        return entry.MapName;
    }

    private static bool IsReservedTypingKey(Keys key) =>
        key is Keys.A or Keys.D or Keys.W or Keys.S or Keys.C or Keys.X or Keys.Space or
            Keys.F or Keys.H or Keys.I or Keys.J or Keys.K or Keys.L or Keys.M or Keys.O or Keys.Q or Keys.R or Keys.U or Keys.E or Keys.T or
            Keys.Left or Keys.Right or Keys.Up or Keys.Down or
            Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End or Keys.F1 or Keys.Enter or Keys.Escape or Keys.Tab or Keys.Add or
            Keys.OemQuestion or
            Keys.OemOpenBrackets or Keys.OemCloseBrackets or Keys.OemPlus;

    private static MTexture? TryTexture(Atlas atlas, params string[] keys) {
        foreach (string key in keys) {
            if (atlas.Has(key)) {
                return atlas[key];
            }
        }

        return null;
    }

    private static MTexture? TimeIconTexture() {
        MTexture? guiIcon = TryTexture(GFX.Gui, "time", "timer", "clock", "journal/time");
        if (guiIcon != null) {
            return guiIcon;
        }

        Atlas? journal = LoadJournalAtlas();
        return journal == null ? null : TryTexture(journal, "time");
    }

    private static Atlas? LoadJournalAtlas() {
        if (journalAtlasLoadAttempted) {
            return journalAtlas;
        }

        journalAtlasLoadAttempted = true;
        try {
            string journalPath = Path.Combine(Engine.ContentDirectory, "Graphics", "Atlases", "Journal");
            journalAtlas = Atlas.FromAtlas(journalPath, Atlas.AtlasDataFormat.Packer);
        } catch (Exception exception) {
            Logger.Log(LogLevel.Warn, nameof(BetterSavesModule), "Could not load native journal time icon: " + exception.Message);
        }

        return journalAtlas;
    }

    private static MTexture? MapPreviewTexture(SaveDeckEntry entry, bool backLayer) {
        string suffix = backLayer ? "_back" : "";
        string? vanillaKey = entry.LastAreaId switch {
            0 => "intro",
            1 => "city",
            2 => "oldsite",
            3 => "resort",
            4 => "cliffside",
            5 => "temple",
            6 => "reflection",
            7 => "Summit",
            8 => "core",
            9 => "farewell",
            _ => null
        };

        if (vanillaKey != null) {
            return TryTexture(GFX.Gui, "areas/" + vanillaKey + suffix);
        }

        string sidKey = AreaTextureKey(entry.LastAreaSid);
        if (sidKey.Length > 0) {
            MTexture? sidTexture = TryTexture(GFX.Gui, "areas/" + sidKey + suffix);
            if (sidTexture != null) {
                return sidTexture;
            }
        }

        return TryTexture(GFX.Gui, "areas/null" + suffix, "areas/steam" + suffix);
    }

    private static string AreaTextureKey(string sid) {
        if (string.IsNullOrWhiteSpace(sid)) {
            return "";
        }

        int slash = sid.LastIndexOf('/');
        return slash >= 0 ? sid.Substring(slash + 1) : sid;
    }

    private static void DrawTextureFit(MTexture texture, float x, float y, float width, float height, Color color) {
        float scale = Math.Min(width / texture.Width, height / texture.Height);
        Vector2 center = new Vector2(x + width / 2f, y + height / 2f);
        texture.DrawCentered(center, color, scale);
    }

    private static void DrawText(string text, Vector2 position, float scale, Color color, Vector2? justify = null, float stroke = 0f) {
        Vector2 actualJustify = justify ?? Vector2.Zero;
        Vector2 actualScale = Vector2.One * scale;
        if (stroke > 0f) {
            ActiveFont.DrawOutline(text, position, actualJustify, actualScale, color, stroke, Outline);
        } else {
            ActiveFont.Draw(text, position, actualJustify, actualScale, color);
        }
    }

    private void UpdateSelectedDetail(bool force = false) {
        if (!force && lastSelection == deckSelection) {
            return;
        }

        lastSelection = deckSelection;
        if (!force) {
            selectionPulse = 1f;
        }
    }

    private List<SaveDeckEntry> FilterEntries(List<SaveDeckEntry> source) {
        return activeFilter switch {
            DeckFilter.Pinned => source.FindAll(entry => entry.IsPinned && !entry.IsArchived),
            DeckFilter.InProgress => source.FindAll(entry => !entry.IsArchived && entry.IsInProgress),
            DeckFilter.Complete => source.FindAll(entry => !entry.IsArchived && entry.IsComplete),
            DeckFilter.Modded => source.FindAll(entry => !entry.IsArchived && entry.IsModded),
            DeckFilter.Archived => source.FindAll(entry => entry.IsArchived),
            DeckFilter.All => source,
            _ => source.FindAll(entry => !entry.IsPinned && !entry.IsArchived)
        };
    }

    private static string FilterLabel(DeckFilter filterMode) {
        return filterMode switch {
            DeckFilter.Pinned => "Pinned",
            DeckFilter.InProgress => "In Progress",
            DeckFilter.Complete => "Complete",
            DeckFilter.Modded => "Modded",
            DeckFilter.Archived => "Archived",
            DeckFilter.All => "All",
            _ => "Recent"
        };
    }

    private static string SortLabel(SaveDeckSort sortMode) {
        return sortMode switch {
            SaveDeckSort.Slot => "Slot sort",
            SaveDeckSort.Name => "Name sort",
            SaveDeckSort.Completion => "Completion sort",
            SaveDeckSort.LastChapter => "Chapter sort",
            _ => "Recent sort"
        };
    }

    private void MoveFilter(int direction) {
        int filterCount = Enum.GetValues(typeof(DeckFilter)).Length;
        int next = ((int) activeFilter + direction + filterCount) % filterCount;
        activeFilter = (DeckFilter) next;
        filterEase = 0f;
        selectionPulse = 0f;
        Audio.Play(direction > 0 ? SFX.ui_main_savefile_roll_down : SFX.ui_main_savefile_roll_up);
        RebuildMenu(preserveSelection: false);
    }

    private void MoveSort() {
        BetterSavesModuleSettings settings = BetterSavesModule.Settings;
        int sortCount = Enum.GetValues(typeof(SaveDeckSort)).Length;
        settings.SortMode = (SaveDeckSort) (((int) settings.SortMode + 1) % sortCount);
        BetterSavesModule.Instance.SaveSettings();
        filterEase = 0f;
        Audio.Play(SFX.ui_main_savefile_roll_down);
        RebuildMenu(preserveSelection: true);
    }

    private void ToggleHelp() {
        showHelp = !showHelp;
        Audio.Play(SFX.ui_main_button_select);
    }

    private static bool NewFileButtonPressed() =>
        MInput.Keyboard.Pressed(Keys.M) ||
        MInput.Keyboard.Pressed(Keys.Add) ||
        MInput.Keyboard.Pressed(Keys.OemPlus);

    private void CreateNewFile() {
        try {
            OpenNativeNewFileSlot(SaveDeckCatalog.NextFreeSlot());
        } catch (Exception exception) {
            Logger.Log(LogLevel.Error, nameof(BetterSavesModule), "Could not open native new file flow: " + exception);
            ShowMessage("Could not open the native new file menu.");
        }
    }

    private void OpenNativeNewFileSlot(int slot) {
        RemoveMenu();
        activeEntry = null;
        menuMode = MenuMode.Deck;

        OuiFileSelect nativeFileSelect = Overworld.GetUI<OuiFileSelect>();
        if (nativeFileSelect == null) {
            ShowMessage("Native file select is not available.");
            return;
        }

        // Force Celeste's file select to reload so the freshly computed empty slot exists,
        // then preselect it after the native enter animation has finished. From there,
        // Celeste owns Begin/Rename/Assist/Variant exactly like the stock menu.
        OuiFileSelect.Loaded = false;
        nativeFileSelect.SlotIndex = slot;
        Add(new Coroutine(SelectNativeNewFileSlot(nativeFileSelect, slot)));
        Audio.Play(SFX.ui_main_button_select);
        Overworld.Goto<OuiFileSelect>();
    }

    private IEnumerator SelectNativeNewFileSlot(OuiFileSelect nativeFileSelect, int slot) {
        while (Overworld.Current != nativeFileSelect || !nativeFileSelect.Focused || !OuiFileSelect.Loaded) {
            yield return null;
        }

        if (slot < 0 || slot >= nativeFileSelect.Slots.Length || nativeFileSelect.Slots[slot] == null) {
            Logger.Log(
                LogLevel.Error,
                nameof(BetterSavesModule),
                "Native file select did not expose expected empty slot " + slot.ToString(CultureInfo.InvariantCulture) + ".");
            Overworld.Goto<OuiBetterSaves>();
            yield break;
        }

        nativeFileSelect.SlotIndex = slot;
        nativeFileSelect.SelectSlot(reset: true);
    }

    private void SelectSlot(int slot) {
        if (continueEntry?.Slot == slot) {
            deckSelection = 0;
            UpdateSelectedDetail(force: true);
            return;
        }

        for (int index = 0; index < entries.Count; index++) {
            if (entries[index].Slot == slot) {
                deckSelection = index + (continueEntry != null ? 1 : 0);
                UpdateSelectedDetail(force: true);
                return;
            }
        }
    }

    private void MoveSelectionPage(int direction) {
        int step = Math.Sign(direction);
        if (step == 0) {
            return;
        }

        MoveDeckSelection(step * VisibleRows);
    }

    private void MoveDeckSelection(int direction) {
        int itemCount = DeckItemCount;
        if (itemCount <= 0) {
            Audio.Play(SFX.ui_main_button_invalid);
            return;
        }

        int previous = deckSelection;
        deckSelection = Calc.Clamp(deckSelection + direction, 0, itemCount - 1);
        if (deckSelection == previous) {
            Audio.Play(SFX.ui_main_button_invalid);
            return;
        }

        selectionPulse = 1f;
        Audio.Play(direction > 0 ? SFX.ui_main_savefile_roll_down : SFX.ui_main_savefile_roll_up);
        UpdateSelectedDetail(force: true);
    }

    private void OpenOptionsForCurrent() {
        SaveDeckEntry? selected = FindSelectedEntry();
        if (selected != null) {
            OpenOptions(selected);
        } else {
            Audio.Play(SFX.ui_main_button_invalid);
        }
    }

    private SaveDeckEntry? FindSelectedEntry() {
        if (menuMode != MenuMode.Deck && activeEntry != null) {
            return activeEntry;
        }

        if (continueEntry != null && deckSelection == 0) {
            return continueEntry;
        }

        int entryIndex = deckSelection - (continueEntry != null ? 1 : 0);
        return entryIndex >= 0 && entryIndex < entries.Count ? entries[entryIndex] : null;
    }

    private int DeckItemCount => (continueEntry != null ? 1 : 0) + entries.Count;

    private void OpenOptions(SaveDeckEntry entry) {
        Audio.Play(SFX.ui_main_button_select);

        RemoveMenu();
        activeEntry = entry;
        menuMode = MenuMode.Options;
        menu = CreatePopupMenu();
        menu.OnESC = RebuildMenu;
        menu.OnCancel = RebuildMenu;
        menu.Add(new TextMenu.Header("Save Options"));
        menu.Add(new TextMenu.SubHeader(entry.Name));
        menu.Add(CenteredButton("Play", () => Play(entry)));
        menu.Add(CenteredButton("Duplicate", () => Duplicate(entry)));
        menu.Add(CenteredButton("Rename", () => Rename(entry)));
        menu.Add(CenteredButton(entry.IsPinned ? "Unpin" : "Pin", () => TogglePin(entry)));
        menu.Add(CenteredButton(entry.IsArchived ? "Restore from Archive" : "Archive", () => ToggleArchive(entry)));
        if (entry.HasBackup) {
            menu.Add(CenteredButton("Restore Backup", () => RestoreBackup(entry)));
        }
        menu.Add(CenteredButton("Details", () => ShowDetails(entry)));
        menu.Add(CenteredButton("Permanent Delete", () => ConfirmPermanentDelete(entry)));
        menu.Add(CenteredButton("Back", RebuildMenu));
        Scene.Add(menu);
    }

    private TextMenu CreatePopupMenu() =>
        new() {
            Focused = true,
            AutoScroll = true,
            MinWidth = 900f
        };

    private TextMenu.Button CenteredButton(string label, Action action) {
        TextMenu.Button button = new(label) {
            AlwaysCenter = true
        };
        button.Pressed(() => {
            BlockDeckInputAfterPopup();
            action();
        });
        return button;
    }

    private void BlockDeckInputAfterPopup() {
        deckInputBlockTimer = Math.Max(deckInputBlockTimer, 0.12f);
        deckConfirmReleaseLock = true;
        Input.MenuConfirm.ConsumePress();
        Input.MenuConfirm.ConsumeBuffer();
    }

    private static bool ConfirmInputHeld() =>
        Input.MenuConfirm.Check ||
        MInput.Keyboard.Check(Keys.Enter) ||
        MInput.Keyboard.Check(Keys.C);

    private void Play(SaveDeckEntry entry) {
        if (entry.Data == null) {
            Audio.Play(SFX.ui_main_button_invalid);
            return;
        }

        Audio.Play(SFX.ui_main_savefile_begin);
        SaveData.Start(entry.Data, entry.Slot);
        if (SaveData.Instance.CurrentSession != null && SaveData.Instance.CurrentSession.InArea) {
            LevelEnter.Go(SaveData.Instance.CurrentSession, fromSaveData: true);
        } else {
            Overworld.Goto<OuiChapterSelect>();
        }
    }

    private void Duplicate(SaveDeckEntry entry) {
        SaveDeckCatalog.Duplicate(entry);
        Audio.Play(SFX.ui_main_message_confirm);
        RebuildMenu();
    }

    private void Rename(SaveDeckEntry entry) {
        if (entry.Data == null) {
            Audio.Play(SFX.ui_main_button_invalid);
            return;
        }

        renamingEntry = entry;
        renameText = entry.Name;
        ShowRenameMenu();
    }

    private void ShowRenameMenu() {
        RemoveMenu();
        activeEntry = renamingEntry;
        menuMode = MenuMode.Rename;
        menu = CreatePopupMenu();
        menu.OnESC = CancelRename;
        menu.OnCancel = CancelRename;
        menu.Add(new TextMenu.Header("Rename"));
        menu.Add(new TextMenu.SubHeader(renameText.Length == 0 ? "_" : renameText));
        menu.Add(new TextMenu.SubHeader("type a name, backspace edits, enter accepts"));
        menu.Add(CenteredButton("Accept", AcceptRename));
        menu.Add(CenteredButton("Cancel", CancelRename));
        Scene.Add(menu);
    }

    private void UpdateRenameInput() {
        if (Input.MenuCancel.Pressed || Input.ESC.Pressed || Input.Pause.Pressed) {
            CancelRename();
            return;
        }

        if (UpdateNameText(ref renameText)) {
            ShowRenameMenu();
        }
    }

    private static bool UpdateNameText(ref string text) {
        bool changed = false;
        foreach (Keys key in MInput.Keyboard.CurrentState.GetPressedKeys()) {
            if (!MInput.Keyboard.Pressed(key)) {
                continue;
            }

            if (key == Keys.Back && text.Length > 0) {
                text = text.Substring(0, text.Length - 1);
                changed = true;
            } else if (key == Keys.Space && text.Length < SaveDeckCatalog.MaxSaveNameLength) {
                text += " ";
                changed = true;
            } else if (key >= Keys.A && key <= Keys.Z && text.Length < SaveDeckCatalog.MaxSaveNameLength) {
                text += key.ToString();
                changed = true;
            } else if (key >= Keys.D0 && key <= Keys.D9 && text.Length < SaveDeckCatalog.MaxSaveNameLength) {
                text += ((int) key - (int) Keys.D0).ToString(CultureInfo.InvariantCulture);
                changed = true;
            }
        }

        return changed;
    }

    private void AcceptRename() {
        if (renamingEntry == null) {
            return;
        }

        string normalized = renameText.Trim();
        if (normalized.Length == 0) {
            Audio.Play(SFX.ui_main_button_invalid);
            return;
        }

        SaveDeckCatalog.Rename(renamingEntry, normalized);
        Audio.Play(SFX.ui_main_rename_entry_accept);
        renamingEntry = null;
        renameText = "";
        RebuildMenu();
    }

    private void CancelRename() {
        SaveDeckEntry? entry = renamingEntry;
        renamingEntry = null;
        renameText = "";
        if (entry != null) {
            OpenOptions(entry);
        } else {
            RebuildMenu();
        }
    }

    private void TogglePin(SaveDeckEntry entry) {
        BetterSavesModuleSettings settings = BetterSavesModule.Settings;
        if (!settings.PinnedSlots.Remove(entry.Slot)) {
            settings.PinnedSlots.Add(entry.Slot);
        }

        BetterSavesModule.Instance.SaveSettings();
        RebuildMenu();
    }

    private void ToggleArchive(SaveDeckEntry entry) {
        BetterSavesModuleSettings settings = BetterSavesModule.Settings;
        if (entry.IsArchived) {
            settings.ArchivedSlots.Remove(entry.Slot);
        } else {
            settings.ArchivedSlots.Add(entry.Slot);
        }

        BetterSavesModule.Instance.SaveSettings();
        RebuildMenu();
    }

    private void RestoreBackup(SaveDeckEntry entry) {
        SaveDeckCatalog.RestoreBackup(entry);
        RebuildMenu();
    }

    private void ShowDetails(SaveDeckEntry entry) {
        ShowMessage(entry.DetailLabel, () => OpenOptions(entry));
    }

    private void ConfirmPermanentDelete(SaveDeckEntry entry) {
        RemoveMenu();
        activeEntry = entry;
        menuMode = MenuMode.ConfirmDelete;
        menu = CreatePopupMenu();
        menu.OnESC = () => OpenOptions(entry);
        menu.OnCancel = () => OpenOptions(entry);
        menu.Add(new TextMenu.Header("Permanent Delete"));
        menu.Add(new TextMenu.SubHeader("Delete \"" + entry.Name + "\"? This cannot be undone."));
        menu.Add(CenteredButton("Cancel", () => OpenOptions(entry)));
        menu.Add(CenteredButton("Delete File", () => {
            if (SaveDeckCatalog.PermanentlyDelete(entry)) {
                BetterSavesModule.Settings.PinnedSlots.Remove(entry.Slot);
                BetterSavesModule.Settings.ArchivedSlots.Remove(entry.Slot);
                BetterSavesModule.Instance.SaveSettings();
                RebuildMenu();
            } else {
                ShowMessage("Could not delete \"" + entry.Name + "\".", () => OpenOptions(entry));
            }
        }));
        Scene.Add(menu);
    }

    private void ShowMessage(string message, Action? back = null) {
        RemoveMenu();
        menuMode = MenuMode.Message;
        activeEntry = null;
        menu = CreatePopupMenu();
        Action backAction = back ?? RebuildMenu;
        messageBackAction = backAction;
        menu.OnESC = backAction;
        menu.OnCancel = backAction;
        menu.Add(new TextMenu.Header("BetterSaves"));
        menu.Add(new TextMenu.SubHeader(message));
        menu.Add(CenteredButton("Back", backAction));
        Scene.Add(menu);
    }

    private void RemoveMenu() {
        if (menu == null) {
            return;
        }

        menu.Visible = false;
        menu.Active = false;
        menu.RemoveSelf();
        menu = null;
    }

    private void HandleCancel() {
        Audio.Play(SFX.ui_main_button_back);

        if (menuMode == MenuMode.Deck) {
            Overworld.Goto<OuiMainMenu>();
        } else if (menuMode == MenuMode.ConfirmDelete && activeEntry != null) {
            OpenOptions(activeEntry);
        } else if (menuMode == MenuMode.Message && messageBackAction != null) {
            messageBackAction();
        } else {
            RebuildMenu();
        }
    }
}
