var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if(scene.path!="Assets/Scenes/TerrainRoad_Lookdev.unity"||UnityEditor.EditorApplication.isPlaying) throw new System.Exception("Expected target scene in Edit Mode.");
var terrain=UnityEngine.Object.FindFirstObjectByType<UnityEngine.Terrain>();
if(terrain==null||terrain.terrainData==null||terrain.GetComponent<UnityEngine.TerrainCollider>()==null) throw new System.Exception("Terrain/collider missing.");
var data=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>("Assets/Art/Environment/TerrainRoadLookdev/RoadsideV1/TerrainData/TD_TerrainRoad_RoadsideV1.asset");
var sourceData=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainData>("Assets/Art/Environment/TerrainRoadLookdev/TerrainData/TD_TerrainRoad_ReliefV3.asset");
var grass=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TerrainLayer>("Assets/Art/Environment/TerrainRoadLookdev/RoadsideV1/TerrainData/TL_RoadsideV1_Grass.terrainlayer");
if(data==null||sourceData==null||grass==null) throw new System.Exception("Roadside clone, source, or grass layer missing.");
terrain.terrainData=data;
terrain.GetComponent<UnityEngine.TerrainCollider>().terrainData=data;
if(data.terrainLayers.Length!=4) throw new System.Exception("Expected four terrain layers, found "+data.terrainLayers.Length);
var layers=(UnityEngine.TerrainLayer[])data.terrainLayers.Clone();
if(sourceData.terrainLayers.Length!=4) throw new System.Exception("Source layer stack changed unexpectedly.");
var oldRoadLayers=new UnityEngine.TerrainLayer[3]; for(int i=0;i<3;i++){oldRoadLayers[i]=sourceData.terrainLayers[i+1];if(layers[i+1]!=oldRoadLayers[i]) throw new System.Exception("Road layer reference mismatch at "+(i+1));}
layers[0]=grass; data.terrainLayers=layers;
for(int i=0;i<3;i++) if(data.terrainLayers[i+1]!=oldRoadLayers[i]) throw new System.Exception("Road layer reference changed at "+i);
var alpha=sourceData.GetAlphamaps(0,0,sourceData.alphamapWidth,sourceData.alphamapHeight);
double changed=0; int modified=0; int laneChanged=0;
double[] px={14,14.4,15.5,17,17.6,17.1,16.3,16}; double[] pz={3,6.5,10,13,16.8,20,24,28};
double[,] centers={{17.05,9.62,0.68,0},{13.85,10.55,0.60,0},{18.15,11.52,0.0,0.72},{14.88,12.48,0.74,0},{18.93,13.50,0.58,0},{15.68,14.47,0.0,0.62},{19.23,15.49,0.70,0},{15.89,16.48,0.0,0.70},{18.65,17.46,0.62,0},{15.27,18.45,0.0,0.60}};
double Smooth(double a,double b,double v){double t=System.Math.Max(0,System.Math.Min(1,(v-a)/(b-a)));return t*t*(3-2*t);}
double Dist(double x,double z){double best=1e9;for(int i=0;i<px.Length-1;i++){double dx=px[i+1]-px[i],dz=pz[i+1]-pz[i],q=((x-px[i])*dx+(z-pz[i])*dz)/(dx*dx+dz*dz);q=System.Math.Max(0,System.Math.Min(1,q));double ex=x-(px[i]+q*dx),ez=z-(pz[i]+q*dz);best=System.Math.Min(best,System.Math.Sqrt(ex*ex+ez*ez));}return best;}
for(int az=0;az<data.alphamapHeight;az++) for(int ax=0;ax<data.alphamapWidth;ax++) {
 double x=((double)ax+0.5)/data.alphamapWidth*data.size.x+terrain.transform.position.x;
 double z=((double)az+0.5)/data.alphamapHeight*data.size.z+terrain.transform.position.z;
 double d=Dist(x,z); double zone=Smooth(9,9.7,z)*(1-Smooth(18.3,19,z));
 double wave=0.5+0.5*System.Math.Sin(x*0.71+System.Math.Sin(z*0.39)*1.6)*System.Math.Cos(z*0.53-x*0.17);
 double outerEdge=1.92+wave*0.12;
 double edge=Smooth(1.28,1.48,d)*(1-Smooth(outerEdge-0.18,outerEdge,d));
 double blend=zone*edge*0.72;
 if(blend<=0.0005) continue;
 double old0=alpha[az,ax,0],old1=alpha[az,ax,1],old2=alpha[az,ax,2],old3=alpha[az,ax,3];
 double earthPatch=0,stonePatch=0;
 for(int k=0;k<centers.GetLength(0);k++){double dx=x-centers[k,0],dz=z-centers[k,1];double r=System.Math.Sqrt(dx*dx+dz*dz);double p=1-Smooth(0.30,0.78,r);earthPatch=System.Math.Max(earthPatch,p*centers[k,2]);stonePatch=System.Math.Max(stonePatch,p*centers[k,3]);}
 double desiredEarth=0.015+0.28*earthPatch;
 double desiredStone=0.008+0.13*stonePatch;
 double desiredGrass=0.68+0.14*wave;
 double targetRoad=System.Math.Max(0,1-desiredGrass-desiredEarth-desiredStone);
 double originalRoad=old1+old2+old3;
 double[] target=new double[4]; target[0]=desiredGrass; target[2]=desiredStone; target[3]=desiredEarth;
 if(originalRoad>0.0001){target[1]=targetRoad*old1/originalRoad;target[2]+=targetRoad*old2/originalRoad;target[3]+=targetRoad*old3/originalRoad;}else target[1]=targetRoad;
 double sum=target[0]+target[1]+target[2]+target[3];
 for(int l=0;l<4;l++){double next=alpha[az,ax,l]*(1-blend)+target[l]*blend/sum; changed+=System.Math.Abs(next-alpha[az,ax,l]); alpha[az,ax,l]=(float)next;}
 modified++;
 if(d<1.0 && System.Math.Abs(alpha[az,ax,0]-old0)>1e-6) laneChanged++;
}
data.SetAlphamaps(0,0,alpha);
UnityEditor.EditorUtility.SetDirty(data); UnityEditor.AssetDatabase.SaveAssetIfDirty(data);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
return "terrain assigned; layers="+string.Join(",",System.Array.ConvertAll(data.terrainLayers,l=>l.name))+"; changedAlphaCells="+modified+"; laneChanged="+laneChanged+"; meanAbsoluteWeightDelta="+(changed/(data.alphamapWidth*data.alphamapHeight*4)).ToString("G8",System.Globalization.CultureInfo.InvariantCulture);
