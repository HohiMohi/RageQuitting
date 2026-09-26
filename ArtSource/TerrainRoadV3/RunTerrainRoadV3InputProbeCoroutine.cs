((System.Action)(() => {
if(!UnityEditor.EditorApplication.isPlaying) throw new System.Exception("Probe requires Play Mode.");
var player=UnityEngine.GameObject.Find("PlayerNew");
var controller=player?player.GetComponent("FirstPersonController") as UnityEngine.MonoBehaviour:null;
var input=player?player.GetComponent("PlayerInputNew"):null;
var camera=UnityEngine.Camera.main;
var terrain=UnityEngine.Object.FindFirstObjectByType<UnityEngine.Terrain>();
var switcher=terrain?terrain.GetComponent("TerrainRoadV3RuntimeSwitcher"):null;
if(player==null||controller==null||!controller.enabled||input==null||camera==null||!camera.isActiveAndEnabled||switcher==null) throw new System.Exception("Player, enabled movement, input, camera, or V3 switcher missing.");
var moveMethod=input.GetType().GetMethod("GetMoveVectorValue");
var isAProperty=switcher.GetType().GetProperty("IsVariantA");
if(moveMethod==null||isAProperty==null) throw new System.Exception("Expected input/variant API missing.");
var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("RoadV3CoroutineKeyboard");
var oldPosition=player.transform.position; var oldRotation=player.transform.rotation; var oldCameraLocalRotation=camera.transform.localRotation;
var errors=new System.Collections.Generic.List<string>();
UnityEngine.Application.LogCallback onLog=(message,stack,kind)=>{if(kind==UnityEngine.LogType.Error||kind==UnityEngine.LogType.Exception||kind==UnityEngine.LogType.Assert)errors.Add(kind+":"+message);};
UnityEngine.Application.logMessageReceived+=onLog;
var gameViewType=System.Type.GetType("UnityEditor.GameView, UnityEditor");
if(gameViewType!=null){var gameView=UnityEditor.EditorWindow.GetWindow(gameViewType); if(gameView!=null)gameView.Focus();}
System.Action cleanup=()=>{UnityEngine.Application.logMessageReceived-=onLog;try{UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);}catch{}player.transform.position=oldPosition;player.transform.rotation=oldRotation;camera.transform.localRotation=oldCameraLocalRotation;UnityEngine.Physics.SyncTransforms();};
System.Collections.IEnumerator Probe(){
 int gameFrames=0; bool initialB=!(bool)isAProperty.GetValue(switcher,null),aSelected=false,bSelected=false,keyPressed=false; UnityEngine.Vector2 move0=UnityEngine.Vector2.zero,move1=move0; UnityEngine.Vector3 p0=oldPosition,c0=camera.transform.position,p1=p0,c1=c0;
 try{
  for(int i=0;i<2;i++){yield return null;gameFrames++;}
  UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.F6));
  for(int i=0;i<2;i++){yield return null;gameFrames++;}
  aSelected=(bool)isAProperty.GetValue(switcher,null);
  UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
  for(int i=0;i<3;i++){yield return null;gameFrames++;}
  p0=player.transform.position;c0=camera.transform.position;
  keyPressed=keyboard.wKey.isPressed;move0=(UnityEngine.Vector2)moveMethod.Invoke(input,null);
  for(int i=0;i<18;i++){yield return null;gameFrames++; if(i==8)move1=(UnityEngine.Vector2)moveMethod.Invoke(input,null);}
  p1=player.transform.position;c1=camera.transform.position;
  UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());
  for(int i=0;i<2;i++){yield return null;gameFrames++;}
  UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.F7));
  for(int i=0;i<2;i++){yield return null;gameFrames++;}
  bSelected=!(bool)isAProperty.GetValue(switcher,null);
  float horizontal=UnityEngine.Vector2.Distance(new UnityEngine.Vector2(p0.x,p0.z),new UnityEngine.Vector2(p1.x,p1.z));
  float cameraDelta=UnityEngine.Vector3.Distance(c0,c1);
  bool passed=initialB&&aSelected&&bSelected&&keyPressed&&move0.y>.5f&&move1.y>.5f&&horizontal>.1f&&cameraDelta>.01f&&controller.enabled&&errors.Count==0;
  string report="{\"passed\":"+(passed?"true":"false")+",\"mode\":\"queued keyboard input sampled inside MonoBehaviour coroutine across actual PlayerLoop frames\""+
   ",\"gameFrames\":"+gameFrames+",\"initialVariantB\":"+(initialB?"true":"false")+",\"F6SelectedA\":"+(aSelected?"true":"false")+",\"F7SelectedB\":"+(bSelected?"true":"false")+
   ",\"WPressed\":"+(keyPressed?"true":"false")+",\"moveSamples\":[\""+move0.ToString("F2")+"\",\""+move1.ToString("F2")+"\"]"+
   ",\"playerHorizontalDeltaM\":"+horizontal.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+",\"cameraDeltaM\":"+cameraDelta.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+
   ",\"movementControllerEnabled\":"+(controller.enabled?"true":"false")+",\"consoleErrors\":"+errors.Count+",\"focusRequested\":"+(gameViewType!=null?"true":"false")+"}";
  System.IO.File.WriteAllText("Artifacts/TerrainRoadV3/TerrainRoadV3_InputProbe_Attempt2.json",report,new System.Text.UTF8Encoding(false));
  if(!passed)UnityEngine.Debug.LogError("TerrainRoadV3 coroutine input probe failed: "+report);else UnityEngine.Debug.Log("TerrainRoadV3 coroutine input probe passed: "+report);
 }finally{cleanup();}
}
controller.StartCoroutine(Probe());
System.IO.File.WriteAllText("Artifacts/TerrainRoadV3/TerrainRoadV3_InputProbe_Attempt2.json","{\"status\":\"running\"}",new System.Text.UTF8Encoding(false));
}))();
