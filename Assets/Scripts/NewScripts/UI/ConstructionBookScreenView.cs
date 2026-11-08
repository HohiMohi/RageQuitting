using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen construction book laid out after the Figma "Construction Book v2" spreads. The hierarchy is built in
/// code inside a 1920x1080 design frame; every position below is in design pixels measured from the frame's top-left.
/// </summary>
public sealed class ConstructionBookScreenView : MonoBehaviour, IConstructionBookView
{
    private static readonly Vector2 DesignSize = new Vector2(1920f, 1080f);

    private const float LeftPageCenterX = 640.5f;
    private const float RightPageCenterX = 1292.5f;

    private const float MaterialsX = 380f, MaterialsY = 504f, MaterialsWidth = 520f, MaterialsMaxHeight = 262f;
    private const float MaterialRowHeight = 76.5f, MaterialRowGap = 4f, MaterialIconSize = 56f;
    private const float MaterialTextX = 4f + MaterialIconSize + 14f, MaterialTotalRight = MaterialsWidth - 8f, MaterialTotalWidth = 90f;

    private const float StepsX = 1007f, StepsY = 222f, StepsWidth = 570f, StepsMaxHeight = 538f;
    private const float ColumnHeaderHeight = 22f;
    private const float StepPaddingY = 10f, StepPaddingLeft = 2f, StepPaddingRight = 4f, StepGap = 12f;
    private const float StepNumberSize = 34f, InstructionLineHeight = 27.3f;
    private const float ToolIconSize = 22f, ToolRowGap = 4f, ToolLabelGap = 6f, ToolGap = 14f;
    private const float SlotWidth = 30f, SlotHeight = 28f, SlotGap = 6f, CheckSize = 22f;
    private const float RuleThickness = 1.5f;

    private const float DisabledButtonAlpha = 0.45f, DisabledKeyHintAlpha = 0.4f;

    public RectTransform Pages { get; private set; }
    public CanvasGroup PagesGroup { get; private set; }
    public Button CloseButton { get; private set; }
    public Button PreviousButton { get; private set; }
    public Button NextButton { get; private set; }

    private TextMeshProUGUI title;
    private Image partImage;
    private GameObject requiredCount;
    private TextMeshProUGUI requiredCountValue;
    private TextMeshProUGUI requiredCountUnit;
    private TextMeshProUGUI materialsHeader;
    private RectTransform materialsRoot;
    private RectTransform stepsRoot;
    private TextMeshProUGUI pageNumber;
    private CanvasGroup previousGroup;
    private CanvasGroup nextGroup;
    private TextMeshProUGUI previousKeyHint;
    private TextMeshProUGUI nextKeyHint;

    private static Color Ink => ConstructionBookScreenTheme.Ink;
    private static Color InkLight => ConstructionBookScreenTheme.InkLight;
    private static Color Rust => ConstructionBookScreenTheme.Rust;
    private static Color Hint => ConstructionBookScreenTheme.Hint;

    public void Build()
    {
        if (Pages != null) return;
        RectTransform root = (RectTransform)transform;
        Image backdrop = GetComponent<Image>();
        if (backdrop == null) backdrop = gameObject.AddComponent<Image>();
        // The book painting has this backdrop colour baked around it, so the backdrop stays opaque.
        backdrop.color = ConstructionBookScreenTheme.Backdrop;

        RectTransform frame = NewRect("Frame", root);
        frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
        frame.sizeDelta = DesignSize;
        frame.anchoredPosition = Vector2.zero;

        AddImage("OpenBook", frame, "Book", 192f, 6f, 1536f, 1024f);

        Pages = NewRect("Pages", frame);
        Pages.anchorMin = Vector2.zero;
        Pages.anchorMax = Vector2.one;
        Pages.offsetMin = Pages.offsetMax = Vector2.zero;
        PagesGroup = Pages.gameObject.AddComponent<CanvasGroup>();
        BuildLeftPage(Pages);
        BuildRightPage(Pages);

        // One right-pointing plank is mirrored for every sign, as in Figma.
        CloseButton = AddBannerButton("CloseButton", frame, "CLOSE", 24f, 20f, 227f, 83f, 32f,
            new Vector2(118.5f, 41.5f), new Vector3(-1f, 1f, 1f), out _);
        PreviousButton = AddBannerButton("PreviousButton", frame, "PREV", 4f, 470f, 190.68f, 69.79f, 26.88f,
            new Vector2(99.3f, 34.88f), new Vector3(-1f, 1f, 1f), out previousGroup);
        NextButton = AddBannerButton("NextButton", frame, "NEXT", 1725.32f, 470f, 190.68f, 69.79f, 26.88f,
            new Vector2(90.74f, 34.88f), new Vector3(1f, -1f, 1f), out nextGroup);
        previousKeyHint = AddCenteredText("PreviousKeyHint", frame, "A", 24f, Hint, 99f, 546f, 60f, 33f);
        nextKeyHint = AddCenteredText("NextKeyHint", frame, "D", 24f, Hint, 1820.5f, 546f, 60f, 33f);
        AddCenteredText("ControlsHint", frame, "Click a box to assign a step   •   A / D  turn page   •   Esc  close",
            20f, Hint, 960f, 1036f, 900f, 27f);
        SetNavigation(true, true);
    }

    public void SetNavigation(bool canGoPrevious, bool canGoNext)
    {
        SetNavigationState(PreviousButton, previousGroup, previousKeyHint, canGoPrevious);
        SetNavigationState(NextButton, nextGroup, nextKeyHint, canGoNext);
    }

    public void RenderWithCompletionSummary(ConstructionBookCatalogSO.Entry entry, int count, int pageIndex, int pageCount,
        IReadOnlyList<ConstructionBookMaterialLine> materials, bool materialsAvailable,
        IReadOnlyList<ConstructionBookRosterMember> roster, ulong localClientId,
        Func<int, int, ulong, bool> isAssigned, Action<int, int, ulong, bool> requestAssignment,
        Func<bool> isPageTransitioning, bool worldView, string completionSummary, bool perStockPlate = false)
    {
        if (Pages == null) Build();

        title.text = entry != null ? entry.title ?? string.Empty : "Data unavailable";
        Sprite part = entry != null ? entry.partSprite : null;
        partImage.sprite = ConstructionBookScreenTheme.HasArt(part) ? part : null;
        partImage.enabled = partImage.sprite != null;
        requiredCount.SetActive(entry != null && !perStockPlate);
        requiredCountValue.text = "x" + count;
        requiredCountUnit.text = count == 1 ? "part" : "parts";
        materialsHeader.text = perStockPlate ? "COST PER STOCK PLATE" : "MATERIALS";
        pageNumber.text = pageCount > 0 ? $"— {pageIndex + 1} / {pageCount} —" : string.Empty;

        Clear(materialsRoot);
        FitToHeight(materialsRoot, BuildMaterials(materials, materialsAvailable, perStockPlate), MaterialsMaxHeight);
        Clear(stepsRoot);
        float stepsHeight = BuildSteps(entry, pageIndex, roster, localClientId, isAssigned, requestAssignment,
            isPageTransitioning, worldView, completionSummary);
        FitToHeight(stepsRoot, stepsHeight, StepsMaxHeight);
    }

    private void BuildLeftPage(RectTransform page)
    {
        title = AddCenteredText("Title", page, string.Empty, 46f, Ink, LeftPageCenterX, 132f, 580f, 63f);
        title.enableAutoSizing = true;
        title.fontSizeMin = 28f;
        title.fontSizeMax = 46f;
        AddImage("TitleDivider", page, "Divider", 450f, 196f, 380f, 16f);

        // Pinned part card, tilted by Figma's rotate(-3deg) around its centre.
        RectTransform card = NewRect("PartIllustration", page);
        card.anchorMin = card.anchorMax = new Vector2(0f, 1f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(210f, 213f);
        card.anchoredPosition = new Vector2(515.43f, -332.86f);
        card.localEulerAngles = new Vector3(0f, 0f, 3f);
        AddImage("Shadow", card, "PartCardShadow", -9f, -7f, 234f, 237f);
        AddImage("Card", card, "PartCard", 0f, 0f, 210f, 213f);
        partImage = AddImage("PartImage", card, null, 28f, 36f, 158f, 154f);
        partImage.preserveAspect = true;
        partImage.enabled = false;
        AddImage("Nail", card, "Nail", 99f, 10f, 12f, 12f);

        RectTransform countRoot = Place(NewRect("RequiredCount", page), 719f, 250f, 112f, 156f);
        requiredCount = countRoot.gameObject;
        AddCenteredText("ToBuild", countRoot, "TO BUILD", 22f, InkLight, 56f, 0f, 200f, 30f);
        requiredCountValue = AddCenteredText("Count", countRoot, string.Empty, 84f, Ink, 56f, 22f, 200f, 115f);
        requiredCountUnit = AddCenteredText("Unit", countRoot, "parts", 20f, InkLight, 56f, 129f, 200f, 27f);

        materialsHeader = AddCenteredText("MaterialsHeader", page, "MATERIALS", 30f, Ink, LeftPageCenterX, 458f, 520f, 41f);
        materialsRoot = PlaceTopCenter(NewRect("Materials", page), MaterialsX + MaterialsWidth * 0.5f, MaterialsY,
            MaterialsWidth, MaterialsMaxHeight);
        AddCenteredText("Footer", page, "Goblin Bridge Builders – Construction Book", 15f, InkLight, LeftPageCenterX, 774f, 520f, 20f);
    }

    private void BuildRightPage(RectTransform page)
    {
        AddCenteredText("StepsTitle", page, "Steps", 46f, Ink, RightPageCenterX, 132f, 400f, 63f);
        AddImage("StepsDivider", page, "Divider", 1102f, 196f, 380f, 16f);
        stepsRoot = PlaceTopCenter(NewRect("Steps", page), StepsX + StepsWidth * 0.5f, StepsY, StepsWidth, StepsMaxHeight);
        pageNumber = AddCenteredText("PageNumber", page, string.Empty, 18f, InkLight, 1292f, 768f, 300f, 25f);
    }

    private float BuildMaterials(IReadOnlyList<ConstructionBookMaterialLine> materials, bool available, bool perStockPlate)
    {
        if (!available || materials == null)
        {
            AddText("Unavailable", materialsRoot, "Data unavailable", 24f, Ink, TextAlignmentOptions.TopLeft,
                MaterialTextX, 12f, MaterialsWidth - MaterialTextX, 32f);
            return 44f;
        }

        float y = 0f;
        for (int i = 0; i < materials.Count; i++)
        {
            if (i > 0) y += MaterialRowGap;
            AddMaterialRow(materials[i], y, perStockPlate);
            y += MaterialRowHeight;
        }
        return y;
    }

    private void AddMaterialRow(ConstructionBookMaterialLine line, float y, bool perStockPlate)
    {
        RectTransform row = Place(NewRect("MaterialRow", materialsRoot), 0f, y, MaterialsWidth, MaterialRowHeight);
        AddDashedRule(row, MaterialRowHeight - RuleThickness, MaterialsWidth);

        BaseResourceSO resource = line.Resource;
        string resourceName = resource != null ? resource.resourceName ?? string.Empty : null;
        Sprite icon = ConstructionBookScreenTheme.MaterialIcon(resourceName);
        if (icon == null && resource != null && ConstructionBookScreenTheme.HasArt(resource.icon)) icon = resource.icon;
        Image iconImage = AddImage("Icon", row, null, 4f, (MaterialRowHeight - MaterialIconSize) * 0.5f - 1f, MaterialIconSize, MaterialIconSize);
        iconImage.sprite = icon;
        iconImage.preserveAspect = true;
        iconImage.enabled = icon != null;

        bool showTotal = !perStockPlate && resource != null;
        float nameWidth = (showTotal ? MaterialTotalRight - MaterialTotalWidth - 14f : MaterialTotalRight) - MaterialTextX;
        TextMeshProUGUI name = AddText("Name", row, resource != null ? resourceName : "Data unavailable", 24f, Ink,
            TextAlignmentOptions.TopLeft, MaterialTextX, 12.25f, nameWidth, 31f);
        name.overflowMode = TextOverflowModes.Ellipsis;
        if (resource != null)
            AddText("PerUnit", row, $"{line.PerUnit} each", 15f, InkLight, TextAlignmentOptions.TopLeft, MaterialTextX, 43.25f, nameWidth, 19f);
        if (!showTotal) return;
        float totalX = MaterialTotalRight - MaterialTotalWidth;
        AddText("Total", row, line.Total.ToString(), 36f, Ink, TextAlignmentOptions.TopRight, totalX, 8.75f, MaterialTotalWidth, 46f);
        AddText("TotalLabel", row, "total", 13f, InkLight, TextAlignmentOptions.TopRight, totalX, 48.75f, MaterialTotalWidth, 17f);
    }

    private float BuildSteps(ConstructionBookCatalogSO.Entry entry, int pageIndex, IReadOnlyList<ConstructionBookRosterMember> roster,
        ulong localClientId, Func<int, int, ulong, bool> isAssigned, Action<int, int, ulong, bool> requestAssignment,
        Func<bool> isPageTransitioning, bool worldView, string completionSummary)
    {
        int members = roster != null ? roster.Count : 0;
        float slotsWidth = members > 0 ? members * SlotWidth + (members - 1) * SlotGap : 0f;
        float slotsX = StepsWidth - StepPaddingRight - slotsWidth;
        float y = ColumnHeaderHeight;

        if (entry == null || entry.steps == null || entry.steps.Length == 0)
        {
            AddText("Unavailable", stepsRoot, "Data unavailable", 20f, Ink, TextAlignmentOptions.TopLeft,
                StepPaddingLeft, y + StepPaddingY, StepsWidth - StepPaddingLeft - StepPaddingRight, InstructionLineHeight);
            y += InstructionLineHeight + StepPaddingY * 2f;
        }
        else
        {
            for (int m = 0; m < members; m++)
            {
                ConstructionBookRosterMember member = roster[m];
                bool isLocal = member.ClientId == localClientId;
                AddText("Column", stepsRoot, ColumnLabel(member, isLocal), 11f, isLocal ? Rust : InkLight, TextAlignmentOptions.Top,
                    slotsX + m * (SlotWidth + SlotGap) - 6f, 0f, SlotWidth + 12f, 16f);
            }
            for (int i = 0; i < entry.steps.Length; i++)
                y += AddStepRow(entry.steps[i], i, y, pageIndex, roster, localClientId, slotsX, isAssigned, requestAssignment,
                    isPageTransitioning, worldView);
        }

        if (!string.IsNullOrWhiteSpace(completionSummary))
        {
            const float summaryWidth = StepsWidth - 30f;
            TextMeshProUGUI summary = AddText("CompletionSummary", stepsRoot, completionSummary, 16f, InkLight,
                TextAlignmentOptions.Top, 15f, y + 10f, summaryWidth, 0f);
            summary.textWrappingMode = TextWrappingModes.Normal;
            float height = summary.GetPreferredValues(completionSummary, summaryWidth, 0f).y;
            summary.rectTransform.sizeDelta = new Vector2(summaryWidth, height);
            y += 10f + height;
        }
        return y;
    }

    private float AddStepRow(ConstructionBookCatalogSO.Step step, int stepIndex, float y, int pageIndex,
        IReadOnlyList<ConstructionBookRosterMember> roster, ulong localClientId, float slotsX,
        Func<int, int, ulong, bool> isAssigned, Action<int, int, ulong, bool> requestAssignment,
        Func<bool> isPageTransitioning, bool worldView)
    {
        int members = roster != null ? roster.Count : 0;
        const float contentX = StepPaddingLeft + StepNumberSize + StepGap;
        float contentRight = members > 0 ? slotsX - StepGap : StepsWidth - StepPaddingRight;
        float contentWidth = contentRight - contentX;

        RectTransform row = NewRect("Step", stepsRoot);
        string instruction = step != null ? step.instruction ?? string.Empty : "Data unavailable";
        TextMeshProUGUI text = AddText("Instruction", row, instruction, 20f, Ink, TextAlignmentOptions.TopLeft, contentX, 0f, contentWidth, 0f);
        text.textWrappingMode = TextWrappingModes.Normal;
        float textHeight = Mathf.Max(InstructionLineHeight, text.GetPreferredValues(instruction, contentWidth, 0f).y);

        List<ConstructionBookCatalogSO.Requirement> tools = new List<ConstructionBookCatalogSO.Requirement>();
        if (step != null && step.requirements != null)
            foreach (ConstructionBookCatalogSO.Requirement requirement in step.requirements)
                if (requirement != null && (!string.IsNullOrWhiteSpace(requirement.displayName) || ConstructionBookScreenTheme.HasArt(requirement.sprite)))
                    tools.Add(requirement);

        float contentHeight = textHeight + (tools.Count > 0 ? ToolRowGap + ToolIconSize : 0f);
        float inner = Mathf.Max(StepNumberSize, Mathf.Max(contentHeight, members > 0 ? SlotHeight : 0f));
        float rowHeight = inner + StepPaddingY * 2f + RuleThickness;
        Place(row, 0f, y, StepsWidth, rowHeight);

        float contentTop = StepPaddingY + (inner - contentHeight) * 0.5f;
        Place(text.rectTransform, contentX, contentTop, contentWidth, textHeight);

        Image ring = AddImage("StepNumber", row, "Ring", StepPaddingLeft, StepPaddingY + (inner - StepNumberSize) * 0.5f, StepNumberSize, StepNumberSize);
        ring.color = Ink;
        AddText("Number", ring.rectTransform, (stepIndex + 1).ToString(), 18f, Ink, TextAlignmentOptions.Center, 0f, 0f, StepNumberSize, StepNumberSize);

        float toolX = contentX;
        float toolY = contentTop + textHeight + ToolRowGap;
        foreach (ConstructionBookCatalogSO.Requirement requirement in tools)
        {
            if (contentRight - toolX <= ToolIconSize) break;
            Sprite icon = ConstructionBookScreenTheme.ToolIcon(requirement.displayName);
            if (icon == null && ConstructionBookScreenTheme.HasArt(requirement.sprite)) icon = requirement.sprite;
            if (icon != null)
            {
                Image toolIcon = AddImage("Icon", row, null, toolX, toolY, ToolIconSize, ToolIconSize);
                toolIcon.sprite = icon;
                toolIcon.preserveAspect = true;
                toolX += ToolIconSize + ToolLabelGap;
            }
            string label = requirement.displayName ?? string.Empty;
            TextMeshProUGUI name = AddText("Tool", row, label, 15f, InkLight, TextAlignmentOptions.Left, toolX, toolY, 0f, ToolIconSize);
            name.overflowMode = TextOverflowModes.Ellipsis;
            // +1 keeps rounding from ellipsising a label that exactly fits.
            float labelWidth = Mathf.Max(0f, Mathf.Min(name.GetPreferredValues(label).x + 1f, contentRight - toolX));
            name.rectTransform.sizeDelta = new Vector2(labelWidth, ToolIconSize);
            toolX += labelWidth + ToolGap;
        }

        float slotTop = StepPaddingY + (inner - SlotHeight) * 0.5f;
        for (int m = 0; m < members; m++)
            AddSlot(row, slotsX + m * (SlotWidth + SlotGap), slotTop, roster[m], localClientId, pageIndex, stepIndex,
                isAssigned, requestAssignment, isPageTransitioning, worldView);

        AddDashedRule(row, rowHeight - RuleThickness, StepsWidth);
        return rowHeight;
    }

    private static void AddSlot(RectTransform row, float x, float y, ConstructionBookRosterMember member, ulong localClientId,
        int spreadIndex, int stepIndex, Func<int, int, ulong, bool> isAssigned, Action<int, int, ulong, bool> requestAssignment,
        Func<bool> isPageTransitioning, bool worldView)
    {
        bool isLocal = member.ClientId == localClientId;
        bool assigned = isAssigned != null && isAssigned(spreadIndex, stepIndex, member.ClientId);
        Image box = AddImage("Slot", row, isLocal ? "SlotYou" : "SlotOther", x, y, SlotWidth, SlotHeight);
        box.raycastTarget = true;
        Image check = AddImage("Check", box.rectTransform, isLocal ? "CheckRust" : "CheckInk",
            (SlotWidth - CheckSize) * 0.5f, (SlotHeight - CheckSize) * 0.5f, CheckSize, CheckSize);
        check.color = assigned ? Color.white : Color.clear;

        Toggle toggle = box.gameObject.AddComponent<Toggle>();
        toggle.transition = Selectable.Transition.None;
        toggle.navigation = new Navigation { mode = Navigation.Mode.None };
        toggle.targetGraphic = box;
        toggle.graphic = check;
        toggle.interactable = !worldView && requestAssignment != null;
        toggle.SetIsOnWithoutNotify(assigned);
        toggle.onValueChanged.AddListener(desired =>
        {
            // Keep the visible box authoritative until the replicated assignment arrives.
            toggle.SetIsOnWithoutNotify(assigned);
            check.color = assigned ? Color.white : Color.clear;
            if (worldView || requestAssignment == null || isPageTransitioning != null && isPageTransitioning()) return;
            requestAssignment(spreadIndex, stepIndex, member.ClientId, desired);
        });
    }

    private static string ColumnLabel(ConstructionBookRosterMember member, bool isLocal)
    {
        if (isLocal) return "YOU";
        const string playerPrefix = "Player ";
        string label = member.Label ?? string.Empty;
        return label.StartsWith(playerPrefix, StringComparison.Ordinal)
            ? "P" + label.Substring(playerPrefix.Length)
            : label.ToUpperInvariant();
    }

    private static void SetNavigationState(Button button, CanvasGroup group, TextMeshProUGUI keyHint, bool enabled)
    {
        if (button == null) return;
        button.interactable = enabled;
        group.alpha = enabled ? 1f : DisabledButtonAlpha;
        Color hint = Hint;
        hint.a = enabled ? 1f : DisabledKeyHintAlpha;
        keyHint.color = hint;
    }

    private static Button AddBannerButton(string name, Transform parent, string label, float x, float y, float width, float height,
        float fontSize, Vector2 labelCenter, Vector3 bannerScale, out CanvasGroup group)
    {
        RectTransform root = Place(NewRect(name, parent), x, y, width, height);
        group = root.gameObject.AddComponent<CanvasGroup>();
        Image banner = AddImage("Banner", root, "Banner", 0f, 0f, width, height);
        banner.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        banner.rectTransform.anchoredPosition = new Vector2(width * 0.5f, -height * 0.5f);
        banner.rectTransform.localScale = bannerScale;
        banner.raycastTarget = true;

        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = banner;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.9f, 0.8f);
        colors.pressedColor = new Color(0.82f, 0.74f, 0.68f);
        colors.selectedColor = Color.white;
        colors.disabledColor = Color.white; // disabled signs fade through their CanvasGroup instead
        button.colors = colors;

        AddText("Label", root, label, fontSize, Color.white, TextAlignmentOptions.Center,
            labelCenter.x - width * 0.5f, labelCenter.y - height * 0.5f, width, height);
        return button;
    }

    private static void AddDashedRule(RectTransform parent, float y, float width)
    {
        Image rule = AddImage("Rule", parent, "Dash", 0f, y, width, RuleThickness);
        rule.type = Image.Type.Tiled;
        rule.pixelsPerUnitMultiplier = 2f; // Dash.png is drawn at 2x
        rule.color = ConstructionBookScreenTheme.DashedRule;
    }

    private static void FitToHeight(RectTransform container, float contentHeight, float maxHeight)
    {
        float scale = contentHeight > maxHeight ? maxHeight / contentHeight : 1f;
        container.localScale = new Vector3(scale, scale, 1f);
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static RectTransform Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, -y);
        return rect;
    }

    private static RectTransform PlaceTopCenter(RectTransform rect, float centerX, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(centerX, -y);
        return rect;
    }

    private static Image AddImage(string name, Transform parent, string spriteName, float x, float y, float width, float height)
    {
        Image image = Place(NewRect(name, parent), x, y, width, height).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        if (spriteName != null)
        {
            image.sprite = ConstructionBookScreenTheme.LoadSprite(spriteName);
            image.enabled = image.sprite != null;
        }
        return image;
    }

    private static TextMeshProUGUI AddText(string name, Transform parent, string value, float size, Color color,
        TextAlignmentOptions alignment, float x, float y, float width, float height)
    {
        TextMeshProUGUI text = Place(NewRect(name, parent), x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset font = ConstructionBookScreenTheme.Font;
        if (font != null) text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    private static TextMeshProUGUI AddCenteredText(string name, Transform parent, string value, float size, Color color,
        float centerX, float y, float width, float height) =>
        AddText(name, parent, value, size, color, TextAlignmentOptions.Top, centerX - width * 0.5f, y, width, height);

    private static void Clear(RectTransform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            child.gameObject.SetActive(false);
            // Detach first so a second render in the same frame never sees the old toggles.
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }
    }
}
