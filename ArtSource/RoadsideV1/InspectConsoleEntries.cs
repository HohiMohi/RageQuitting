var assembly=typeof(UnityEditor.EditorWindow).Assembly;var t=assembly.GetType("UnityEditor.LogEntries");var entry=assembly.GetType("UnityEditor.LogEntry");var b=new System.Text.StringBuilder();
b.AppendLine("LogEntries="+(t==null?"null":t.FullName)+" LogEntry="+(entry==null?"null":entry.FullName));
if(t!=null)foreach(var m in t.GetMethods(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))if(m.Name.Contains("Entr")||m.Name.Contains("Count"))b.AppendLine(m.ToString());
if(entry!=null)foreach(var f in entry.GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))b.AppendLine("field="+f.FieldType.Name+" "+f.Name);
return b.ToString();
