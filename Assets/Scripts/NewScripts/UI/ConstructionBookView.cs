using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ConstructionBookView : MonoBehaviour
{
    private const float StepsHeadingSize = 28f;
    private const float InstructionSize = 22f;
    private const float ToolIconSize = 22f;
    private const float ToolNameSize = 18f;
    private const float CheckboxSize = 18f;
    private const float PlayerLabelSize = 12f;
    private const float PlayerCellWidth = 64f;
    private const float PlayerCellHeight = 40f;
    private const float PlayerRowHeight = 42f;
    private const float CardPadding = 6f;
    private const float InternalSpacing = 3f;
    private const float CardGap = 6f;

    private TextMeshProUGUI title;
    private Image partImage;
    private RectTransform materialsRoot;
    private RectTransform stepsRoot;
    public RectTransform TurningPagePivot { get; private set; }

    public void Build(Color parchment, Color ink)
    {
        RectTransform root = gameObject.GetComponent<RectTransform>();
        Image background = gameObject.GetComponent<Image>();
        if (background == null) background = gameObject.AddComponent<Image>();
        background.color = parchment;
        RectTransform left = Panel("Left", root, Vector2.zero, new Vector2(.5f, 1f));
        RectTransform right = Panel("Right", root, new Vector2(.5f, 0f), Vector2.one);
        right.pivot = new Vector2(0f, .5f);
        right.offsetMin = right.offsetMax = Vector2.zero;
        Image rightPage = right.gameObject.AddComponent<Image>();
        rightPage.color = parchment;
        rightPage.raycastTarget = false;
        TurningPagePivot = right;
        title = Text("Title", left, 42f, ink, TextAlignmentOptions.Center, 76f);
        title.rectTransform.anchorMin = new Vector2(.05f, .84f);
        title.rectTransform.anchorMax = new Vector2(.95f, .98f);
        partImage = Node("PartImage", left, typeof(Image)).GetComponent<Image>();
        RectTransform imageRt = (RectTransform)partImage.transform;
        imageRt.anchorMin = new Vector2(.28f, .56f);
        imageRt.anchorMax = new Vector2(.72f, .82f);
        imageRt.offsetMin = imageRt.offsetMax = Vector2.zero;
        partImage.preserveAspect = true;
        materialsRoot = LayoutArea("Materials", left, new Vector2(.06f, .05f), new Vector2(.94f, .54f), 8f, 10);
        stepsRoot = LayoutArea("Steps", right, new Vector2(.04f, .04f), new Vector2(.96f, .96f), CardGap, 6);
    }

    public void Render(ConstructionBookCatalogSO.Entry entry, int count, int pageIndex, int pageCount, IReadOnlyList<ConstructionBookMaterialLine> materials, bool materialsAvailable, IReadOnlyList<ConstructionBookRosterMember> roster, ulong localClientId)
    {
        Clear(materialsRoot);
        Clear(stepsRoot);
        title.text = entry != null ? $"{entry.title}  x{count}\n<size=60%>Page {pageIndex + 1} / {pageCount}</size>" : "Data unavailable";
        partImage.sprite = entry != null ? entry.partSprite : null;
        partImage.enabled = partImage.sprite != null;
        Text("MATERIALS", materialsRoot, 34f, Ink(), TextAlignmentOptions.Left, 44f);
        if (!materialsAvailable) Text("Data unavailable", materialsRoot, 30f, Ink(), TextAlignmentOptions.Left, 40f);
        else foreach (ConstructionBookMaterialLine line in materials)
        {
            RectTransform row = Horizontal("MaterialRow", materialsRoot, 54f, 7f, TextAnchor.MiddleLeft);
            AddIcon(row, line.Resource != null ? line.Resource.icon : null, 48f);
            Text(line.Resource != null ? $"{line.Resource.resourceName}: {line.PerUnit} each · {line.Total} total" : "Data unavailable", row, 27f, Ink(), TextAlignmentOptions.Left, 40f, true);
        }

        Text("STEPS", stepsRoot, StepsHeadingSize, Ink(), TextAlignmentOptions.Center, 34f);
        if (entry == null || entry.steps == null || entry.steps.Length == 0)
        {
            Text("Data unavailable", stepsRoot, 30f, Ink(), TextAlignmentOptions.Left, 40f);
            return;
        }

        for (int i = 0; i < entry.steps.Length; i++)
        {
            ConstructionBookCatalogSO.Step step = entry.steps[i];
            bool hasRequirements = step.requirements != null && step.requirements.Length > 0;
            float cardHeight = CardPadding * 2f + 28f + PlayerRowHeight + InternalSpacing + (hasRequirements ? 25f + InternalSpacing : 0f);
            RectTransform card = Vertical("Step", stepsRoot, cardHeight, InternalSpacing, new RectOffset(6, 6, 6, 6));
            Image bg = card.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, .12f);
            Text($"{i + 1}. {step.instruction}", card, InstructionSize, Ink(), TextAlignmentOptions.Left, 28f);
            if (hasRequirements)
            {
                RectTransform reqRow = Horizontal("Requirements", card, 25f, InternalSpacing, TextAnchor.MiddleLeft);
                foreach (ConstructionBookCatalogSO.Requirement req in step.requirements)
                {
                    AddIcon(reqRow, req.sprite, ToolIconSize);
                    Text(req.displayName, reqRow, ToolNameSize, Ink(), TextAlignmentOptions.Left, 22f);
                }
            }

            RectTransform players = Horizontal("Players", card, PlayerRowHeight, CardGap, TextAnchor.MiddleCenter);
            players.GetComponent<LayoutElement>().flexibleWidth = 1f;
            foreach (ConstructionBookRosterMember member in roster) AddPlayerCell(players, member, localClientId);
        }
    }

    private static void AddPlayerCell(RectTransform players, ConstructionBookRosterMember member, ulong localClientId)
    {
        bool isLocal = member.ClientId == localClientId;
        RectTransform cell = Vertical("Player", players, PlayerCellHeight, 1f, new RectOffset());
        LayoutElement cellLayout = cell.GetComponent<LayoutElement>();
        cellLayout.preferredWidth = PlayerCellWidth;
        cellLayout.minWidth = PlayerCellWidth;
        cellLayout.flexibleWidth = 0f;
        Image field = cell.gameObject.AddComponent<Image>();
        field.color = isLocal ? new Color(.48f, .16f, .05f, .24f) : new Color(0f, 0f, 0f, .08f);
        field.raycastTarget = false;

        GameObject checkboxObject = Node("Checkbox", cell, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Toggle));
        Image checkbox = checkboxObject.GetComponent<Image>();
        checkbox.color = new Color(1f, 1f, 1f, .08f);
        Outline outline = checkboxObject.GetComponent<Outline>();
        outline.effectColor = isLocal ? new Color(.48f, .16f, .05f) : Ink();
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        GameObject checkObject = Node("Checkmark", checkboxObject.transform, typeof(RectTransform), typeof(Image));
        RectTransform checkRect = (RectTransform)checkObject.transform;
        checkRect.anchorMin = new Vector2(.2f, .2f);
        checkRect.anchorMax = new Vector2(.8f, .8f);
        checkRect.offsetMin = checkRect.offsetMax = Vector2.zero;
        Image check = checkObject.GetComponent<Image>();
        check.color = Color.clear;
        check.raycastTarget = false;
        Toggle toggle = checkboxObject.GetComponent<Toggle>();
        toggle.interactable = false;
        toggle.transition = Selectable.Transition.None;
        toggle.targetGraphic = checkbox;
        toggle.graphic = check;
        toggle.SetIsOnWithoutNotify(false);
        LayoutElement boxLayout = checkboxObject.AddComponent<LayoutElement>();
        boxLayout.minWidth = boxLayout.preferredWidth = CheckboxSize;
        boxLayout.minHeight = boxLayout.preferredHeight = CheckboxSize;
        Text(member.Label, cell, PlayerLabelSize, isLocal ? new Color(.48f, .16f, .05f) : Ink(), TextAlignmentOptions.Center, 15f);
    }

    private static Color Ink() => new Color(.13f, .09f, .05f);
    private static GameObject Node(string name, Transform parent, params System.Type[] components) { GameObject go = new GameObject(name, components); go.transform.SetParent(parent, false); return go; }
    private static RectTransform Panel(string name, RectTransform parent, Vector2 min, Vector2 max) { RectTransform rt = (RectTransform)Node(name, parent, typeof(RectTransform)).transform; rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero; return rt; }
    private static RectTransform LayoutArea(string name, RectTransform parent, Vector2 min, Vector2 max, float spacing, int padding) { RectTransform rt = Panel(name, parent, min, max); VerticalLayoutGroup layout = rt.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = spacing; layout.padding = new RectOffset(padding, padding, padding, padding); layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false; return rt; }
    private static RectTransform Vertical(string name, Transform parent, float height, float spacing, RectOffset padding) { RectTransform rt = (RectTransform)Node(name, parent, typeof(RectTransform)).transform; VerticalLayoutGroup layout = rt.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = spacing; layout.padding = padding; layout.childAlignment = TextAnchor.UpperCenter; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false; layout.childForceExpandWidth = false; LayoutElement element = rt.gameObject.AddComponent<LayoutElement>(); element.minHeight = element.preferredHeight = height; return rt; }
    private static RectTransform Horizontal(string name, Transform parent, float height, float spacing, TextAnchor alignment) { RectTransform rt = (RectTransform)Node(name, parent, typeof(RectTransform)).transform; HorizontalLayoutGroup layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.spacing = spacing; layout.childAlignment = alignment; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false; layout.childForceExpandWidth = false; LayoutElement element = rt.gameObject.AddComponent<LayoutElement>(); element.minHeight = element.preferredHeight = height; return rt; }
    private static TextMeshProUGUI Text(string value, Transform parent, float size, Color color, TextAlignmentOptions alignment, float height, bool flexibleWidth = false) { TextMeshProUGUI text = Node("Text", parent, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>(); text.text = value; text.fontSize = size; text.color = color; text.alignment = alignment; text.textWrappingMode = TextWrappingModes.Normal; LayoutElement element = text.gameObject.AddComponent<LayoutElement>(); element.minHeight = element.preferredHeight = height; if (flexibleWidth) element.flexibleWidth = 1f; return text; }
    private static void AddIcon(Transform parent, Sprite sprite, float size) { Image image = Node("Icon", parent, typeof(RectTransform), typeof(Image)).GetComponent<Image>(); image.sprite = sprite; image.preserveAspect = true; image.enabled = sprite != null; LayoutElement element = image.gameObject.AddComponent<LayoutElement>(); element.minWidth = element.preferredWidth = size; element.minHeight = element.preferredHeight = size; }
    private static void Clear(RectTransform root) { for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject); }
}
