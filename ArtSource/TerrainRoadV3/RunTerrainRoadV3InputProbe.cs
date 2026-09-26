((System.Action)(() => {
if(!UnityEditor.EditorApplication.isPlaying) throw new System.Exception("Probe requires Play Mode.");
var player=UnityEngine.GameObject.Find("PlayerNew");
var controller=player?player.GetComponent("FirstPersonController") as UnityEngine.Behaviour:null;
var input=player?player.GetComponent("PlayerInputNew"):null;
var camera=UnityEngine.Camera.main;
var terrain=UnityEngine.Object.FindFirstObjectByType<UnityEngine.Terrain>();
var switcher=terrain?terrain.GetComponent("TerrainRoadV3RuntimeSwitcher"):null;
if(player==null||controller==null||!controller.enabled||input==null||camera==null||!camera.isActiveAndEnabled||switcher==null) throw new System.Exception("Player, enabled movement, input, camera, or V3 switcher missing.");
var moveMethod=input.GetType().GetMethod("GetMoveVectorValue");
var isAProperty=switcher.GetType().GetProperty("IsVariantA");
if(moveMethod==null||isAProperty==null) throw new System.Exception("Expected input/variant API missing.");
var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("RoadV3VirtualKeyboard");
var oldPosition=player.transform.position; var oldRotation=player.transform.rotation; var oldCameraLocalRotation=camera.transform.localRotation;
var errors=new System.Collections.Generic.List<string>();
UnityEngine.Application.LogCallback onLog=(message,stack,kind)=>{if(kind==UnityEngine.LogType.Error||kind==UnityEngine.LogType.Exception||kind==UnityEngine.LogType.Assert)errors.Add(kind+":"+message);};
UnityEngine.Application.logMessageReceived+=onLog;
var gameViewType=System.Type.GetType("UnityEditor.GameView, UnityEditor");
if(gameViewType!=null){var gameView=UnityEditor.EditorWindow.GetWindow(gameViewType); if(gameView!=null)gameView.Focus();}
int previousFrame=-1,frames=0; bool initialB=false,aSeen=false,bSeen=false; UnityEngine.Vector3 startPlayer=oldPosition,startCamera=camera.transform.position,finalPlayer=oldPosition,finalCamera=startCamera; UnityEngine.Vector2 move0=UnityEngine.Vector2.zero,move1=move0; string error=null;
UnityEditor.EditorApplication.CallbackFunction tick=null;
System.Action cleanup=()=>{UnityEditor.EditorApplication.update-=tick;UnityEngine.Application.logMessageReceived-=onLog;try{UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);}catch{}player.transform.position=oldPosition;player.transform.rotation=oldRotation;camera.transform.localRotation=oldCameraLocalRotation;UnityEngine.Physics.SyncTransforms();};
tick=()=>{try{
 if(!UnityEditor.EditorApplication.isPlaying){error="Play Mode ended before probe finished.";cleanup();return;}
 int current=UnityEngine.Time.frameCount; if(current==previousFrame)return; previousFrame=current; frames++;
 if(frames==4){initialB=!(bool)isAProperty.GetValue(switcher,null);UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.F6));}
 if(frames==7){aSeen=(bool)isAProperty.GetValue(switcher,null);startPlayer=player.transform.position;startCamera=camera.transform.position;UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));}
 if(frames==12)move0=(UnityEngine.Vector2)moveMethod.Invoke(input,null);
 if(frames==32)move1=(UnityEngine.Vector2)moveMethod.Invoke(input,null);
 if(frames==38){finalPlayer=player.transform.position;finalCamera=camera.transform.position;UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());}
 if(frames==42)UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.F7));
 if(frames==45)bSeen=!(bool)isAProperty.GetValue(switcher,null);
 if(frames>=49){
  float playerDelta=UnityEngine.Vector2.Distance(new UnityEngine.Vector2(startPlayer.x,startPlayer.z),new UnityEngine.Vector2(finalPlayer.x,finalPlayer.z)),cameraDelta=UnityEngine.Vector3.Distance(startCamera,finalCamera);
  bool passed=initialB&&aSeen&&bSeen&&move0.y>.5f&&move1.y>.5f&&playerDelta>.1f&&cameraDelta>.01f&&controller.enabled&&errors.Count==0;
  string report="{\"passed\":"+(passed?"true":"false")+",\"mode\":\"queued InputSystem Keyboard state; real scene component Update with active FirstPersonController\""+
   ",\"actualGameFrames\":"+frames+",\"initialVariantB\":"+(initialB?"true":"false")+",\"F6SelectedA\":"+(aSeen?"true":"false")+",\"F7SelectedB\":"+(bSeen?"true":"false")+
   ",\"moveSamples\":[\""+move0.ToString("F2")+"\",\""+move1.ToString("F2")+"\"]"+
   ",\"playerDeltaM\":"+playerDelta.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+",\"cameraDeltaM\":"+cameraDelta.ToString("G8",System.Globalization.CultureInfo.InvariantCulture)+
   ",\"movementControllerEnabled\":"+(controller.enabled?"true":"false")+",\"consoleErrors\":"+errors.Count+",\"focusRequested\":"+(gameViewType!=null?"true":"false")+"}";
  System.IO.File.WriteAllText("Artifacts/TerrainRoadV3/TerrainRoadV3_InputProbe.json",report,new System.Text.UTF8Encoding(false));
  cleanup();
  if(!passed)UnityEngine.Debug.LogError("TerrainRoadV3 real-scene InputSystem probe failed: "+report);else UnityEngine.Debug.Log("TerrainRoadV3 real-scene InputSystem probe passed: "+report);
 }
}catch(System.Exception e){error=e.Message;System.IO.File.WriteAllText("Artifacts/TerrainRoadV3/TerrainRoadV3_InputProbe.json","{\"passed\":false,\"error\":\""+e.Message.Replace("\\","\\\\").Replace("\"","\\\"")+"\",\"actualGameFrames\":"+frames+"}",new System.Text.UTF8Encoding(false));cleanup();UnityEngine.Debug.LogException(e);}};
UnityEditor.EditorApplication.update+=tick;
System.IO.File.WriteAllText("Artifacts/TerrainRoadV3/TerrainRoadV3_InputProbe.json","{\"status\":\"running\"}",new System.Text.UTF8Encoding(false));
}))();
