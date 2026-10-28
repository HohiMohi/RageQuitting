using System.Collections.Generic;
using System;
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
    private WorldViewRenderState lastWorldViewRenderState;
    public RectTransform TurningPagePivot { get; private set; }

    public void Build(Color parchment, Color ink)
    {
        lastWorldViewRenderState = null;
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

    public void Render(ConstructionBookCatalogSO.Entry entry, int count, int pageIndex, int pageCount, IReadOnlyList<ConstructionBookMaterialLine> materials, bool materialsAvailable, IReadOnlyList<ConstructionBookRosterMember> roster, ulong localClientId, Func<int, int, ulong, bool> isAssigned, Action<int, int, ulong, bool> requestAssignment, Func<bool> isPageTransitioning, bool worldView)
    {
        RenderWithCompletionSummary(entry, count, pageIndex, pageCount, materials, materialsAvailable, roster, localClientId,
            isAssigned, requestAssignment, isPageTransitioning, worldView, string.Empty, false);
    }

    public void RenderWithCompletionSummary(ConstructionBookCatalogSO.Entry entry, int count, int pageIndex, int pageCount,
        IReadOnlyList<ConstructionBookMaterialLine> materials, bool materialsAvailable,
        IReadOnlyList<ConstructionBookRosterMember> roster, ulong localClientId,
        Func<int, int, ulong, bool> isAssigned, Action<int, int, ulong, bool> requestAssignment,
        Func<bool> isPageTransitioning, bool worldView, string completionSummary, bool perStockPlate = false)
    {
        if (worldView)
        {
            WorldViewRenderState nextState = WorldViewRenderState.Capture(entry, count, pageIndex, pageCount,
                materials, materialsAvailable, roster, localClientId, isAssigned, completionSummary, perStockPlate);
            if (nextState != null && nextState.HasSameValues(lastWorldViewRenderState)) return;
            lastWorldViewRenderState = nextState;
        }
        else
        {
            // Interactive renders must refresh their Toggle listeners on every call.
            lastWorldViewRenderState = null;
        }

        Clear(materialsRoot);
        Clear(stepsRoot);
        title.text = entry != null ? $"{entry.title}{(perStockPlate ? string.Empty : $"  x{count}")}\n<size=60%>Page {pageIndex + 1} / {pageCount}</size>" : "Data unavailable";
        partImage.sprite = entry != null ? entry.partSprite : null;
        partImage.enabled = partImage.sprite != null;
        Text(perStockPlate ? "COST PER STOCK PLATE" : "MATERIALS", materialsRoot, 34f, Ink(), TextAlignmentOptions.Left, 44f);
        if (!materialsAvailable) Text("Data unavailable", materialsRoot, 30f, Ink(), TextAlignmentOptions.Left, 40f);
        else foreach (ConstructionBookMaterialLine line in materials)
        {
            RectTransform row = Horizontal("MaterialRow", materialsRoot, 54f, 7f, TextAnchor.MiddleLeft);
            AddIcon(row, line.Resource != null ? line.Resource.icon : null, 48f);
            Text(line.Resource != null ? perStockPlate ? $"{line.Resource.resourceName}: {line.PerUnit} each" : $"{line.Resource.resourceName}: {line.PerUnit} each · {line.Total} total" : "Data unavailable", row, 27f, Ink(), TextAlignmentOptions.Left, 40f, true);
        }

        Text("STEPS", stepsRoot, StepsHeadingSize, Ink(), TextAlignmentOptions.Center, 34f);
        if (!string.IsNullOrWhiteSpace(completionSummary))
            Text(completionSummary, stepsRoot, 18f, Ink(), TextAlignmentOptions.Center, 24f);
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
            foreach (ConstructionBookRosterMember member in roster)
                AddPlayerCell(players, member, localClientId, pageIndex, i, isAssigned, requestAssignment, isPageTransitioning, worldView);
        }
    }

    private sealed class WorldViewRenderState
    {
        private readonly string entryTitle;
        private readonly bool entryPresent;
        private readonly Sprite partSprite;
        private readonly int count;
        private readonly int pageIndex;
        private readonly int pageCount;
        private readonly bool materialsAvailable;
        private readonly bool perStockPlate;
        private readonly string completionSummary;
        private readonly MaterialRenderValue[] materials;
        private readonly StepRenderValue[] steps;
        private readonly RosterRenderValue[] roster;
        private readonly bool[][] assignments;

        private WorldViewRenderState(string entryTitle, bool entryPresent, Sprite partSprite, int count, int pageIndex, int pageCount,
            bool materialsAvailable, bool perStockPlate, string completionSummary,
            MaterialRenderValue[] materials, StepRenderValue[] steps, RosterRenderValue[] roster, bool[][] assignments)
        {
            this.entryTitle = entryTitle;
            this.entryPresent = entryPresent;
            this.partSprite = partSprite;
            this.count = count;
            this.pageIndex = pageIndex;
            this.pageCount = pageCount;
            this.materialsAvailable = materialsAvailable;
            this.perStockPlate = perStockPlate;
            this.completionSummary = completionSummary;
            this.materials = materials;
            this.steps = steps;
            this.roster = roster;
            this.assignments = assignments;
        }

        public static WorldViewRenderState Capture(ConstructionBookCatalogSO.Entry entry, int count,
            int pageIndex, int pageCount, IReadOnlyList<ConstructionBookMaterialLine> materialLines,
            bool materialsAvailable, IReadOnlyList<ConstructionBookRosterMember> rosterMembers,
            ulong localClientId, Func<int, int, ulong, bool> isAssigned, string completionSummary,
            bool perStockPlate)
        {
            // Preserve the old render's failure behavior for invalid collections by letting
            // Render build (and fail) instead of caching a partial representation.
            if ((materialsAvailable && materialLines == null) ||
                (entry != null && entry.steps != null && entry.steps.Length > 0 && rosterMembers == null))
                return null;

            if (entry != null && entry.steps != null)
            {
                foreach (ConstructionBookCatalogSO.Step sourceStep in entry.steps)
                {
                    if (sourceStep == null) return null;
                    if (sourceStep.requirements == null) continue;
                    foreach (ConstructionBookCatalogSO.Requirement requirement in sourceStep.requirements)
                        if (requirement == null) return null;
                }
            }

            var materialValues = new List<MaterialRenderValue>();
            if (materialsAvailable)
            {
                for (int i = 0; i < materialLines.Count; i++)
                {
                    ConstructionBookMaterialLine line = materialLines[i];
                    BaseResourceSO resource = line.Resource;
                    materialValues.Add(new MaterialRenderValue(resource != null, resource != null ? resource.resourceName : null,
                        resource != null ? resource.icon : null, line.PerUnit, line.Total));
                }
            }

            ConstructionBookCatalogSO.Step[] sourceSteps = entry != null ? entry.steps : null;
            int stepCount = sourceSteps != null ? sourceSteps.Length : 0;
            var stepValues = new StepRenderValue[stepCount];
            var assignmentValues = new bool[stepCount][];
            for (int stepIndex = 0; stepIndex < stepCount; stepIndex++)
            {
                ConstructionBookCatalogSO.Step step = sourceSteps[stepIndex];
                ConstructionBookCatalogSO.Requirement[] sourceRequirements = step.requirements;
                int requirementCount = sourceRequirements != null ? sourceRequirements.Length : 0;
                var requirementValues = new RequirementRenderValue[requirementCount];
                for (int requirementIndex = 0; requirementIndex < requirementCount; requirementIndex++)
                {
                    ConstructionBookCatalogSO.Requirement requirement = sourceRequirements[requirementIndex];
                    requirementValues[requirementIndex] = new RequirementRenderValue(
                        requirement.displayName, requirement.sprite);
                }
                stepValues[stepIndex] = new StepRenderValue(step.instruction, requirementValues);

                int rosterCount = rosterMembers != null ? rosterMembers.Count : 0;
                assignmentValues[stepIndex] = new bool[rosterCount];
                for (int rosterIndex = 0; rosterIndex < rosterCount; rosterIndex++)
                    assignmentValues[stepIndex][rosterIndex] = isAssigned != null &&
                        isAssigned(pageIndex, stepIndex, rosterMembers[rosterIndex].ClientId);
            }

            int memberCount = rosterMembers != null ? rosterMembers.Count : 0;
            var rosterValues = new RosterRenderValue[memberCount];
            for (int i = 0; i < memberCount; i++)
            {
                ConstructionBookRosterMember member = rosterMembers[i];
                rosterValues[i] = new RosterRenderValue(member.ClientId, member.Label,
                    member.ClientId == localClientId);
            }

            return new WorldViewRenderState(entry != null ? entry.title : null, entry != null,
                entry != null ? entry.partSprite : null, count, pageIndex, pageCount, materialsAvailable,
                perStockPlate, completionSummary, materialValues.ToArray(), stepValues, rosterValues, assignmentValues);
        }

        public bool HasSameValues(WorldViewRenderState other)
        {
            if (other == null || entryPresent != other.entryPresent ||
                !string.Equals(entryTitle, other.entryTitle, StringComparison.Ordinal) ||
                partSprite != other.partSprite || count != other.count || pageIndex != other.pageIndex ||
                pageCount != other.pageCount || materialsAvailable != other.materialsAvailable ||
                perStockPlate != other.perStockPlate ||
                !string.Equals(completionSummary, other.completionSummary, StringComparison.Ordinal) ||
                !SameValues(materials, other.materials) || !SameValues(steps, other.steps) ||
                !SameValues(roster, other.roster) || assignments.Length != other.assignments.Length)
                return false;

            for (int i = 0; i < assignments.Length; i++)
                if (!SameValues(assignments[i], other.assignments[i])) return false;
            return true;
        }

        private static bool SameValues<T>(T[] left, T[] right) where T : IEquatable<T>
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
                if (!left[i].Equals(right[i])) return false;
            return true;
        }

        private static bool SameValues(bool[] left, bool[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i]) return false;
            return true;
        }
    }

    private readonly struct MaterialRenderValue : IEquatable<MaterialRenderValue>
    {
        private readonly bool resourcePresent;
        private readonly string resourceName;
        private readonly Sprite icon;
        private readonly int perUnit;
        private readonly int total;
        public MaterialRenderValue(bool resourcePresent, string resourceName, Sprite icon, int perUnit, int total)
        { this.resourcePresent = resourcePresent; this.resourceName = resourceName; this.icon = icon; this.perUnit = perUnit; this.total = total; }
        public bool Equals(MaterialRenderValue other) =>
            resourcePresent == other.resourcePresent && string.Equals(resourceName, other.resourceName, StringComparison.Ordinal) && icon == other.icon &&
            perUnit == other.perUnit && total == other.total;
    }

    private readonly struct RequirementRenderValue : IEquatable<RequirementRenderValue>
    {
        private readonly string displayName;
        private readonly Sprite sprite;
        public RequirementRenderValue(string displayName, Sprite sprite)
        { this.displayName = displayName; this.sprite = sprite; }
        public bool Equals(RequirementRenderValue other) =>
            string.Equals(displayName, other.displayName, StringComparison.Ordinal) && sprite == other.sprite;
    }

    private sealed class StepRenderValue : IEquatable<StepRenderValue>
    {
        private readonly string instruction;
        private readonly RequirementRenderValue[] requirements;
        public StepRenderValue(string instruction, RequirementRenderValue[] requirements)
        { this.instruction = instruction; this.requirements = requirements; }
        public bool Equals(StepRenderValue other)
        {
            if (other == null || !string.Equals(instruction, other.instruction, StringComparison.Ordinal) ||
                requirements.Length != other.requirements.Length) return false;
            for (int i = 0; i < requirements.Length; i++)
                if (!requirements[i].Equals(other.requirements[i])) return false;
            return true;
        }
    }

    private readonly struct RosterRenderValue : IEquatable<RosterRenderValue>
    {
        private readonly ulong clientId;
        private readonly string label;
        private readonly bool isLocal;
        public RosterRenderValue(ulong clientId, string label, bool isLocal)
        { this.clientId = clientId; this.label = label; this.isLocal = isLocal; }
        public bool Equals(RosterRenderValue other) => clientId == other.clientId && isLocal == other.isLocal &&
            string.Equals(label, other.label, StringComparison.Ordinal);
    }

    private static void AddPlayerCell(RectTransform players, ConstructionBookRosterMember member, ulong localClientId, int spreadIndex, int stepIndex, Func<int, int, ulong, bool> isAssigned, Action<int, int, ulong, bool> requestAssignment, Func<bool> isPageTransitioning, bool worldView)
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
        bool assigned = isAssigned != null && isAssigned(spreadIndex, stepIndex, member.ClientId);
        check.color = assigned ? Ink() : Color.clear;
        check.raycastTarget = false;
        Toggle toggle = checkboxObject.GetComponent<Toggle>();
        toggle.interactable = !worldView && requestAssignment != null;
        toggle.transition = Selectable.Transition.None;
        toggle.targetGraphic = checkbox;
        toggle.graphic = check;
        toggle.SetIsOnWithoutNotify(assigned);
        toggle.onValueChanged.AddListener(desired =>
        {
            if (worldView || requestAssignment == null || isPageTransitioning != null && isPageTransitioning())
            {
                toggle.SetIsOnWithoutNotify(assigned);
                check.color = assigned ? Ink() : Color.clear;
                return;
            }
            // Keep the visible checkbox authoritative until the NetworkList update arrives.
            toggle.SetIsOnWithoutNotify(assigned);
            check.color = assigned ? Ink() : Color.clear;
            requestAssignment(spreadIndex, stepIndex, member.ClientId, desired);
        });
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
    private static void Clear(RectTransform root) { for (int i = root.childCount - 1; i >= 0; i--) { GameObject child = root.GetChild(i).gameObject; child.SetActive(false); Destroy(child); } }
}
