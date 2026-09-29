// Project Rebel Quit — balans na ogrywkę 3 graczy (Tutorial_scene).
// Wrzuć do Assets/Editor/. Menu: Rebel Quit > Balans ogrywki 3P.
// Nie odwołuje się do typów gry w czasie kompilacji (szuka ich po nazwie),
// więc kompiluje się niezależnie od asmdefów projektu.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class RebelQuitBalans3P
{
    const string Menu = "Rebel Quit/Balans ogrywki 3P/";
    const string ExpectedScene = "Tutorial_scene";
    static string BackupPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../BalanceBackups/balans_3p_backup.json"));

    // ---------- backup ----------

    [Serializable] class Entry { public string id; public string path; public string kind; public float f; public int i; public bool b; public string label; }
    [Serializable] class Backup { public string scene; public string date; public List<Entry> entries = new List<Entry>(); }

    static Backup backup;
    static StringBuilder log;
    static int changed, missing;

    // ---------- menu ----------

    [MenuItem(Menu + "Zastosuj", false, 1)]
    static void Apply()
    {
        if (File.Exists(BackupPath))
        {
            EditorUtility.DisplayDialog("Balans 3P",
                "Balans jest już zastosowany (istnieje kopia zapasowa).\n\nNajpierw użyj „Przywróć poprzednie wartości”, żeby nie nałożyć zmian dwa razy.\n\n" + BackupPath, "OK");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != ExpectedScene &&
            !EditorUtility.DisplayDialog("Balans 3P",
                $"Aktywna scena to „{scene.name}”, a balans jest przygotowany dla „{ExpectedScene}”.\n\nKontynuować mimo to?", "Kontynuuj", "Anuluj"))
            return;

        backup = new Backup { scene = scene.name, date = DateTime.Now.ToString("s") };
        log = new StringBuilder("<b>[Balans 3P] Zastosowano:</b>\n");
        changed = 0; missing = 0;

        // 1. Czas poziomu
        foreach (var c in SceneComponents("GameTimerManager"))
            SetFloat(c, "levelDuration", 2400f);

        // 2. Obsada — sockety w scenie
        foreach (var c in SceneComponents("BridgeMountSocket"))
            SetBool(c, "requireRecommendedCarrierCount", true);

        // 2. Obsada — assety części (tylko części wieloosobowe: recommendedCarriers >= 2)
        foreach (var a in Assets("MountableBridgeComponentSO"))
        {
            int rec = GetInt(a, "recommendedCarriers", 1);
            if (rec < 2) continue;
            SetInt(a, "minAmountOfPlayersNeeded", rec >= 3 ? 2 : 1);
            SetFloat(a, "sharedCarryUnderstaffedStaminaDrainPerSecond", 4f);
        }

        // 3. Spawnery bobrów
        foreach (var c in SceneComponents("NPCSpawner"))
        {
            string n = c.gameObject.name;
            bool north = n.IndexOf("North", StringComparison.OrdinalIgnoreCase) >= 0;
            bool south = n.IndexOf("South", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!north && !south) { Note($"pominięto spawner „{n}” (nie North/South)"); continue; }
            SetFloat(c, "initialSpawnDelay", north ? 120f : 240f);
            SetFloat(c, "spawnIntervalMin", 45f);
            SetFloat(c, "spawnIntervalMax", 75f);
        }

        // 3. Próg obrońcy
        var defenderCond = Assets("NPCSpawnCountConditionSO").Where(a => a.name.Contains("BeaverDefender")).ToList();
        if (defenderCond.Count == 0) Missing("asset BeaverDefenderAfterThreeScouts (NPCSpawnCountConditionSO)");
        foreach (var a in defenderCond) SetInt(a, "spawnCountThreshold", 4);

        // 3. Skaut — krótsze śledzenie gracza
        foreach (var a in Assets("BeaverScoutBehaviorSO"))
        {
            SetFloat(a, "followDurationMin", 8f);
            SetFloat(a, "followDurationMax", 15f);
        }

        // 3. Koza — wyłączenie obiektów w scenie
        var goats = SceneComponents("GoatChargeController").Select(c => c.gameObject).Distinct().ToList();
        if (goats.Count == 0) Missing("koza w scenie (GoatChargeController)");
        foreach (var go in goats) SetActive(go, false);

        // 4. Piec — wymagany progres niższy o ~25% (mapowanie po obecnej wartości)
        var furnaceMap = new Dictionary<int, float> { { 400, 300 }, { 550, 400 }, { 700, 525 }, { 800, 600 }, { 450, 350 } };
        foreach (var a in Assets("ProductionRecipeSO"))
        {
            if (GetInt(a, "productType", 0) != 1) continue; // 1 = BaseResource (wyjście pieca)
            int cur = Mathf.RoundToInt(GetFloat(a, "neededProgress", -1));
            if (furnaceMap.TryGetValue(cur, out float next)) SetFloat(a, "neededProgress", next);
            else Note($"receptura „{a.name}”: neededProgress = {cur}, spoza tabeli — bez zmian");
        }

        // zapis
        Directory.CreateDirectory(Path.GetDirectoryName(BackupPath));
        File.WriteAllText(BackupPath, JsonUtility.ToJson(backup, true));
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);

        log.Append($"\nZmieniono: {changed}, nie znaleziono: {missing}.\nKopia: {BackupPath}\n");
        log.Append("<b>Zapisz scenę (Ctrl+S).</b> Uwaga: initialSpawnDelay liczy się od załadowania sceny, nie od startu timera — startujcie timer od razu.");
        Debug.Log(log.ToString());
        Report();
    }

    [MenuItem(Menu + "Przywróć poprzednie wartości", false, 2)]
    static void Restore()
    {
        if (!File.Exists(BackupPath)) { EditorUtility.DisplayDialog("Balans 3P", "Brak kopii zapasowej — nie ma czego przywracać.", "OK"); return; }
        var b = JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath));
        var sb = new StringBuilder("<b>[Balans 3P] Przywrócono:</b>\n");
        int ok = 0, fail = 0;

        foreach (var e in b.entries)
        {
            Object o = null;
            if (GlobalObjectId.TryParse(e.id, out var gid)) o = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
            if (o == null) { fail++; sb.AppendLine($"  ✗ nie znaleziono obiektu: {e.label}"); continue; }

            if (e.kind == "active")
            {
                var go = (GameObject)o;
                Undo.RecordObject(go, "Balans 3P przywróć");
                go.SetActive(e.b);
            }
            else
            {
                var so = new SerializedObject(o);
                var p = so.FindProperty(e.path);
                if (p == null) { fail++; sb.AppendLine($"  ✗ brak pola: {e.label}"); continue; }
                if (e.kind == "float") p.floatValue = e.f;
                else if (e.kind == "int") p.intValue = e.i;
                else if (e.kind == "bool") p.boolValue = e.b;
                so.ApplyModifiedProperties();
            }
            ok++; sb.AppendLine($"  ✓ {e.label}");
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        File.Move(BackupPath, BackupPath.Replace(".json", $"_restored_{DateTime.Now:yyyyMMdd_HHmmss}.json"));
        sb.Append($"\nPrzywrócono: {ok}, błędy: {fail}. <b>Zapisz scenę (Ctrl+S).</b>");
        Debug.Log(sb.ToString());
    }

    [MenuItem(Menu + "Raport wartości do sprawdzenia", false, 20)]
    static void Report()
    {
        var sb = new StringBuilder("<b>[Balans 3P] Do sprawdzenia ręcznie (bez zmian):</b>\n");

        sb.AppendLine("\nunderStaffedPenaltyMultiplier (kierunek sprawdzić w kodzie):");
        foreach (var a in Assets("MountableBridgeComponentSO"))
            sb.AppendLine($"  {a.name}: {GetFloat(a, "underStaffedPenaltyMultiplier", float.NaN)} (recommendedCarriers {GetInt(a, "recommendedCarriers", 0)})");

        sb.AppendLine("\nObrażenia narzędzi (EquippableItemSO):");
        foreach (var a in Assets("EquippableItemSO"))
            sb.AppendLine($"  {a.name}: damage {Show(a, "damage")}, resourceDamage {Show(a, "resourceDamage")}, constructionWorkPower {Show(a, "constructionWorkPower")}");

        sb.AppendLine("\nNPC (NPCDefinitionSO):");
        foreach (var a in Assets("NPCDefinitionSO"))
            sb.AppendLine($"  {a.name}: maxHealth {Show(a, "maxHealth")}, moveSpeed {Show(a, "moveSpeed")}");

        AppendPrefab(sb, "NPC_BeaverScout", "NPCAttackController", "attackDamage");
        AppendPrefab(sb, "NPC_BeaverScout", "NPCHealth", "maxHealth");
        AppendPrefab(sb, "NPC_BeaverDefender", "NPCAttackController", "attackDamage");
        AppendPrefab(sb, "PlayerNew", "PlayerHealth", null);

        Debug.Log(sb.ToString());
    }

    // ---------- helpers: wyszukiwanie ----------

    static readonly Dictionary<string, Type> typeCache = new Dictionary<string, Type>();
    static Type FindType(string name)
    {
        if (typeCache.TryGetValue(name, out var t)) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch { continue; }
            t = types.FirstOrDefault(x => x.Name == name);
            if (t != null) break;
        }
        typeCache[name] = t;
        return t;
    }

    static IEnumerable<Component> SceneComponents(string typeName)
    {
        var t = FindType(typeName);
        if (t == null) { Missing($"typ {typeName}"); return Enumerable.Empty<Component>(); }
        var scene = SceneManager.GetActiveScene();
        var list = Resources.FindObjectsOfTypeAll(t).OfType<Component>()
            .Where(c => c != null && !EditorUtility.IsPersistent(c) && c.gameObject.scene == scene).ToList();
        if (list.Count == 0) Missing($"{typeName} w scenie");
        return list;
    }

    static IEnumerable<Object> Assets(string typeName)
    {
        var t = FindType(typeName);
        if (t == null) { Missing($"typ {typeName}"); return Enumerable.Empty<Object>(); }
        var list = AssetDatabase.FindAssets("t:" + typeName)
            .Select(g => AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(g), t))
            .Where(o => o != null).ToList();
        if (list.Count == 0) Missing($"assety {typeName}");
        return list;
    }

    static void AppendPrefab(StringBuilder sb, string prefabName, string componentType, string field)
    {
        var t = FindType(componentType);
        var guid = AssetDatabase.FindAssets(prefabName + " t:Prefab")
            .FirstOrDefault(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == prefabName);
        if (t == null || guid == null) { sb.AppendLine($"\n{prefabName}/{componentType}: nie znaleziono"); return; }
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
        var comp = go != null ? go.GetComponentInChildren(t, true) : null;
        if (comp == null) { sb.AppendLine($"\n{prefabName}/{componentType}: brak komponentu"); return; }

        sb.AppendLine($"\n{prefabName} / {componentType}:");
        var so = new SerializedObject(comp);
        if (field != null) { sb.AppendLine($"  {field}: {Show(comp, field)}"); return; }
        var p = so.GetIterator();
        for (bool enter = true; p.NextVisible(enter); enter = false)
        {
            if (p.propertyType == SerializedPropertyType.Float) sb.AppendLine($"  {p.name}: {p.floatValue}");
            else if (p.propertyType == SerializedPropertyType.Integer) sb.AppendLine($"  {p.name}: {p.intValue}");
        }
    }

    // ---------- helpers: odczyt ----------

    static SerializedProperty Prop(Object o, string path, out SerializedObject so)
    {
        so = new SerializedObject(o);
        return so.FindProperty(path);
    }

    static float GetFloat(Object o, string path, float fallback) { var p = Prop(o, path, out _); return p != null && p.propertyType == SerializedPropertyType.Float ? p.floatValue : fallback; }
    static int GetInt(Object o, string path, int fallback)
    {
        var p = Prop(o, path, out _);
        if (p == null) return fallback;
        if (p.propertyType == SerializedPropertyType.Integer) return p.intValue;
        if (p.propertyType == SerializedPropertyType.Enum) return p.enumValueIndex;
        return fallback;
    }
    static string Show(Object o, string path)
    {
        var p = Prop(o, path, out _);
        if (p == null) return "—";
        switch (p.propertyType)
        {
            case SerializedPropertyType.Float: return p.floatValue.ToString("0.###");
            case SerializedPropertyType.Integer: return p.intValue.ToString();
            case SerializedPropertyType.Boolean: return p.boolValue.ToString();
            default: return "?";
        }
    }

    // ---------- helpers: zapis z kopią ----------

    static string Label(Object o, string path) =>
        (o is Component c ? $"{c.gameObject.name}/{o.GetType().Name}" : o.name) + "." + path;

    static void SetFloat(Object o, string path, float v)
    {
        var p = Prop(o, path, out var so);
        if (p == null || p.propertyType != SerializedPropertyType.Float) { Missing(Label(o, path)); return; }
        if (Mathf.Approximately(p.floatValue, v)) { Note($"{Label(o, path)} już = {v}"); return; }
        Record(o, path, "float", e => e.f = p.floatValue);
        log.AppendLine($"  ✓ {Label(o, path)}: {p.floatValue} → {v}");
        p.floatValue = v; so.ApplyModifiedProperties(); changed++;
    }

    static void SetInt(Object o, string path, int v)
    {
        var p = Prop(o, path, out var so);
        if (p == null || p.propertyType != SerializedPropertyType.Integer) { Missing(Label(o, path)); return; }
        if (p.intValue == v) { Note($"{Label(o, path)} już = {v}"); return; }
        Record(o, path, "int", e => e.i = p.intValue);
        log.AppendLine($"  ✓ {Label(o, path)}: {p.intValue} → {v}");
        p.intValue = v; so.ApplyModifiedProperties(); changed++;
    }

    static void SetBool(Object o, string path, bool v)
    {
        var p = Prop(o, path, out var so);
        if (p == null || p.propertyType != SerializedPropertyType.Boolean) { Missing(Label(o, path)); return; }
        if (p.boolValue == v) { Note($"{Label(o, path)} już = {v}"); return; }
        Record(o, path, "bool", e => e.b = p.boolValue);
        log.AppendLine($"  ✓ {Label(o, path)}: {p.boolValue} → {v}");
        p.boolValue = v; so.ApplyModifiedProperties(); changed++;
    }

    static void SetActive(GameObject go, bool v)
    {
        if (go.activeSelf == v) { Note($"{go.name} już active = {v}"); return; }
        Record(go, "m_IsActive", "active", e => e.b = go.activeSelf);
        log.AppendLine($"  ✓ {go.name}: active {go.activeSelf} → {v}");
        Undo.RecordObject(go, "Balans 3P");
        go.SetActive(v); changed++;
    }

    static void Record(Object o, string path, string kind, Action<Entry> fill)
    {
        var e = new Entry { id = GlobalObjectId.GetGlobalObjectIdSlow(o).ToString(), path = path, kind = kind, label = Label(o, path) };
        fill(e);
        backup.entries.Add(e);
    }

    static void Missing(string what) { missing++; log?.AppendLine($"  ✗ nie znaleziono: {what}"); }
    static void Note(string what) { log?.AppendLine($"  · {what}"); }
}
