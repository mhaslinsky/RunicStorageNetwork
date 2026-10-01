using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RunicStorage.Build {
 public static partial class BuildAssets {
  const string BuilderSource=Root+"/BuilderCodex/Source";
  const string BuilderAsset=Root+"/RSN_RunicBuilderCodex.prefab",BuilderIcon=Root+"/RSN_BuilderCodexIcon.png";
  const string BuilderFbxHash="5c286fe91a81c1bbc00f052ef702bf72536fbc9d13b4f0c0ce6a1d28cc7d0d01";
  // Offsets are in metres inside the attachment. Vanilla preserves its world
  // scale, then resets only the attach_Hips root's local position and rotation.
  static readonly Vector3 BuilderBeltPosition=new Vector3(-.195f,.20f,0);
  static readonly Quaternion BuilderBeltRotation=Quaternion.Euler(0,90,0);
  const int BuilderBeltTriangles=444;
  static Mesh SaveBuilderMesh(Mesh mesh,string name){
   string directory=Root+"/BuilderCodex/Meshes";Directory.CreateDirectory(directory);AssetDatabase.Refresh();
   string path=directory+"/"+name+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
   if(saved){EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);}
   else{AssetDatabase.CreateAsset(mesh,path);saved=mesh;}return saved;
  }
  static void AddBuilderBelt(GameObject root,Dictionary<string,Material> materials){
   var belt=new GameObject("Belt");belt.transform.SetParent(root.transform,false);
   var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();
   Action<Vector3,Vector3,Vector3,Vector3> quad=(a,b,c,d)=>{
    int i=vertices.Count;vertices.AddRange(new[]{a,b,c,d});uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
    triangles.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});
   };
   // A softly squared leather strap passes through both authored loops at the
   // left hip. It belongs only to attach_Hips, never to the dropped/icon visual.
   Func<float,float,float,Vector3> ring=(angle,y,inset)=>{
    float x=Mathf.Cos(angle),z=Mathf.Sin(angle);
    // Follow the waist on the right, leaving a short flatter section inside
    // the existing suspension loops on the left.
    return new Vector3(Mathf.Sign(x)*Mathf.Pow(Mathf.Abs(x),x<0?.35f:1)*(x<0?.201f-inset:.185f-inset),y,z*(.150f-inset));
   };
   for(int i=0;i<48;i++){
    float a=i*Mathf.PI*2/48,b=(i+1)*Mathf.PI*2/48;
    var lo=ring(a,.181f,0);var hi=ring(a,.219f,0);var nextLo=ring(b,.181f,0);var nextHi=ring(b,.219f,0);
    var innerLo=ring(a,.181f,.006f);var innerHi=ring(a,.219f,.006f);var nextInnerLo=ring(b,.181f,.006f);var nextInnerHi=ring(b,.219f,.006f);
    quad(lo,hi,nextHi,nextLo);quad(innerLo,nextInnerLo,nextInnerHi,innerHi);
    quad(hi,innerHi,nextInnerHi,nextHi);quad(lo,nextLo,nextInnerLo,innerLo);
   }
   Action<string,string> finish=(name,material)=>{
    var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
    var part=new GameObject(name);part.transform.SetParent(belt.transform,false);part.AddComponent<MeshFilter>().sharedMesh=SaveBuilderMesh(mesh,name);
    var renderer=part.AddComponent<MeshRenderer>();renderer.sharedMaterial=materials[material];renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
    vertices.Clear();triangles.Clear();uv.Clear();
   };
   finish("BeltLeather","RBC_HarnessLeather");
   Action<Vector3,Vector3> box=(center,size)=>{
    var l=center-size*.5f;var h=center+size*.5f;
    var a=new Vector3(l.x,l.y,l.z);var b=new Vector3(h.x,l.y,l.z);var c=new Vector3(h.x,h.y,l.z);var d=new Vector3(l.x,h.y,l.z);
    var e=new Vector3(l.x,l.y,h.z);var f=new Vector3(h.x,l.y,h.z);var g=new Vector3(h.x,h.y,h.z);var k=new Vector3(l.x,h.y,h.z);
    quad(a,d,c,b);quad(e,f,g,k);quad(a,e,k,d);quad(b,c,g,f);quad(d,k,g,c);quad(a,b,f,e);
   };
   // Small silver frame and pin on the front, matching the book's fittings.
   box(new Vector3(-.028f,.20f,.155f),new Vector3(.006f,.05f,.009f));
   box(new Vector3(.028f,.20f,.155f),new Vector3(.006f,.05f,.009f));
   box(new Vector3(0,.222f,.155f),new Vector3(.05f,.006f,.009f));
   box(new Vector3(0,.178f,.155f),new Vector3(.05f,.006f,.009f));
   box(new Vector3(0,.20f,.161f),new Vector3(.052f,.004f,.004f));
   finish("BeltSilver","RBC_Silver");
  }
  static Bounds BuilderBounds(GameObject go){
   var rs=go.GetComponentsInChildren<Renderer>();Check(rs.Length>0,"Builder visual empty");
   var bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);return bounds;
  }
  static GameObject BuildBuilderCodex(string output){
   string path=BuilderSource+"/RunicBuilderCodex.fbx";
   using(var sha=SHA256.Create())Check(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").Equals(BuilderFbxHash,StringComparison.OrdinalIgnoreCase),"Builder model differs from reviewed v01 export");
   var records=JsonUtility.FromJson<TerminalMaterials>(File.ReadAllText(BuilderSource+"/materials.json")).materials;
   var palette=JsonUtility.FromJson<TerminalMaterials>(File.ReadAllText(TerminalSource+"/materials.json")).materials;
   var shader=AssetDatabase.LoadAssetAtPath<Shader>(Root+"/CoreRuntime.shader");Check(shader&&!ShaderUtil.ShaderHasError(shader),"Builder shader missing");
   var materials=new System.Collections.Generic.Dictionary<string,Material>();
   foreach(var record in records){
    string slot=record.objects.Single(),asset=Root+"/Materials/"+slot+".mat";
    var mat=AssetDatabase.LoadAssetAtPath<Material>(asset);if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,asset);}
    mat.shader=shader;mat.name=slot;
    var color=TerminalColor(record.base_color);var emission=TerminalColor(record.emission_color);
    if(slot=="RBC_RunesPrimary"||slot=="RBC_RunesSecondary"||slot=="RBC_Crystal"){
     var reference=palette.Single(r=>r.objects[0]==(slot=="RBC_RunesSecondary"?"RST_BookSmallRunes":"RST_BookMainRune"));
     color=TerminalColor(reference.base_color);emission=TerminalColor(reference.emission_color);
    }
    mat.SetVector("_BaseLinear",color);mat.SetVector("_EmissionLinear",emission);
    mat.SetFloat("_Metallic",slot=="RBC_Silver"?.82f:record.metallic);mat.SetFloat("_Smoothness",slot=="RBC_Silver"?.55f:1-record.roughness);
    mat.SetFloat("_EmissionStrength",record.emission_strength);mat.SetFloat("_Cull",record.backface_culling?2:0);
    mat.SetFloat("_UseFacetMask",0);mat.SetColor("_Color",Color.white);mat.SetColor("_EmissionColor",Color.clear);
    EditorUtility.SetDirty(mat);materials.Add(slot,mat);
   }
   var importer=(ModelImporter)AssetImporter.GetAtPath(path);Check(importer,"Builder FBX missing");
   importer.globalScale=1;importer.useFileScale=true;importer.bakeAxisConversion=true;
   importer.importNormals=ModelImporterNormals.Import;importer.importTangents=ModelImporterTangents.None;
   importer.meshCompression=ModelImporterMeshCompression.Off;importer.weldVertices=false;importer.optimizeMeshPolygons=false;importer.optimizeMeshVertices=false;
   importer.isReadable=true;importer.generateSecondaryUV=false;importer.preserveHierarchy=true;importer.addCollider=false;
   importer.importCameras=false;importer.importLights=false;importer.importAnimation=false;importer.animationType=ModelImporterAnimationType.None;
   importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
   foreach(var r in records)importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),r.name),materials[r.objects[0]]);
   importer.SaveAndReimport();var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);Check(model,"Builder import failed");
   var go=new GameObject("RSN_RunicBuilderCodex");
   try{
    var attach=new GameObject("attach");attach.transform.SetParent(go.transform,false);
    var visual=(GameObject)PrefabUtility.InstantiatePrefab(model);visual.transform.SetParent(attach.transform,false);
    PrefabUtility.UnpackPrefabInstance(visual,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);visual.name="Book";
    foreach(var r in visual.GetComponentsInChildren<MeshRenderer>()){
     Check(materials.ContainsKey(r.name),"Unknown builder mesh: "+r.name);r.sharedMaterial=materials[r.name];
     var mesh=r.GetComponent<MeshFilter>().sharedMesh;
     Check(mesh.normals.Length==mesh.vertexCount&&mesh.normals.All(n=>Mathf.Abs(n.sqrMagnitude-1)<.001f),"Builder normals invalid");
     r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;
    }
    var upright=BuilderBounds(visual);Debug.Log("RSN_BUILDER_UPRIGHT "+upright.ToString("F5"));
    Check(Vector3.Distance(upright.size,new Vector3(.25335f,.497f,.1405f))<.0002f,"Builder scale changed");
    var equippedRoot=new GameObject("attach_Hips");equippedRoot.transform.SetParent(go.transform,false);
    var equipped=UnityEngine.Object.Instantiate(visual,equippedRoot.transform,false);equipped.name="Book";
    equipped.transform.localPosition=BuilderBeltPosition;equipped.transform.localRotation=BuilderBeltRotation;
    AddBuilderBelt(equippedRoot,materials);
    equippedRoot.SetActive(false);
    // The floor item lies flat. Keep this transform independent of the belt book.
    visual.transform.localRotation=Quaternion.Euler(90,0,0);
    var bounds=BuilderBounds(go);visual.transform.localPosition=new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z);
    var rigid=visual.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="RBC_Leather"||r.name=="RBC_Silver"||r.name=="RBC_HarnessLeather").ToArray();
    var body=rigid[0].bounds;foreach(var r in rigid)body.Encapsulate(r.bounds);
    var collider=go.AddComponent<BoxCollider>();collider.center=body.center;collider.size=body.size;
    PrefabUtility.SaveAsPrefabAsset(go,BuilderAsset);AssetDatabase.SaveAssets();
    ValidateBuilderVisual(go);
    // Photograph the upright belt silhouette; keep loops readable at inventory size.
    visual.transform.localPosition=Vector3.zero;visual.transform.localRotation=Quaternion.identity;
    bounds=BuilderBounds(go);
    RenderIcon(go,bounds,BuilderIcon,RenderingPath.Forward,256,.33f,new Vector3(3,1.7f,-6));
    AssetDatabase.Refresh();var icon=(TextureImporter)AssetImporter.GetAtPath(BuilderIcon);
    icon.textureType=TextureImporterType.Sprite;icon.spriteImportMode=SpriteImportMode.Single;icon.mipmapEnabled=false;icon.alphaIsTransparency=true;
    icon.textureCompression=TextureImporterCompression.Uncompressed;icon.SaveAndReimport();AssetDatabase.SaveAssets();
    TerminalView(go,Path.Combine(output,"builder-codex-model.png"),new Vector3(3,1.7f,-6),false,framing:.34f);
    TerminalView(go,Path.Combine(output,"builder-codex-back.png"),new Vector3(-3,1.7f,6),false,framing:.34f);
    File.Copy(BuilderIcon,Path.Combine(output,"builder-codex-icon.png"),true);
    UnityEngine.Object.DestroyImmediate(go);
    return UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BuilderAsset));
   }catch{if(go)UnityEngine.Object.DestroyImmediate(go);throw;}
  }
  static void ValidateBuilderVisual(GameObject prefab){
   var floor=prefab.transform.Find("attach");var worn=prefab.transform.Find("attach_Hips");
   Check(floor&&worn&&floor.gameObject.activeSelf&&!worn.gameObject.activeSelf,"Builder attachment visibility invalid");
   Check(prefab.GetComponentsInChildren<MeshRenderer>().Length==9&&prefab.GetComponentsInChildren<MeshRenderer>(true).Length==20,"Builder double floor visual or missing attachment");
   Check(!floor.Find("Belt")&&worn.Find("Belt"),"Belt must exist only on equipped model");
   Check(worn.Find("Belt").GetComponentsInChildren<MeshFilter>(true).Sum(f=>f.sharedMesh.triangles.Length/3)==BuilderBeltTriangles,"Belt geometry missing");
   foreach(var root in new[]{floor,worn}){
    Check(root.Find("Book").GetComponentsInChildren<MeshFilter>(true).Sum(f=>f.sharedMesh.triangles.Length/3)==1389,"Builder book geometry changed");
    Check(root.GetComponentsInChildren<Collider>(true).Length==0&&root.GetComponentsInChildren<Rigidbody>(true).Length==0&&root.GetComponentsInChildren<MonoBehaviour>(true).Length==0,"Equipment visual contains behavior/collisions");
   }
   Check(prefab.GetComponentsInChildren<Collider>(true).Length==1&&prefab.GetComponent<BoxCollider>(),"Builder drop collider missing");
   Check(Mathf.Abs(BuilderBounds(prefab).min.y)<.0001f,"Dropped builder book floats");
   foreach(var r in prefab.GetComponentsInChildren<MeshRenderer>(true))Check(r.sharedMaterial.shader.name!="Custom/Piece"&&!ShaderUtil.ShaderHasError(r.sharedMaterial.shader),"Native/missing material in builder bundle");
  }
  static void ValidateBuilderBundle(AssetBundle bundle,string output){
   var prefab=bundle.LoadAsset<GameObject>(BuilderAsset);Check(prefab&&bundle.LoadAsset<Sprite>(BuilderIcon),"Builder bundle assets missing");
   ValidateBuilderVisual(prefab);
   PreviewBuilderEquipment(prefab,output);
   File.WriteAllText(Path.Combine(output,"BuilderCodexAssetReport.json"),"{\"bookTriangles\":1389,\"beltTriangles\":"+BuilderBeltTriangles+",\"droppedRenderers\":9,\"equippedRenderers\":11,\"beltOnlyWhenEquipped\":true,\"side\":\"character left\",\"attachment\":\"attach_Hips\",\"slot\":\"Utility\",\"runtimeComponentsInBundle\":false,\"bundleReload\":true,\"nativeGameAssetsBundled\":false,\"gameValidated\":false}");
  }
  static string[] PreparedAssetNames()=>new[]{Root+"/RSN_NetworkCore.prefab",Root+"/RSN_CoreIcon.png",Root+"/RSN_RunicRelay.prefab",Root+"/RSN_RelayIcon.png",TerminalAsset,TerminalIcon,CodexAsset,CodexIcon,GatewayAsset,GatewayIcon,BuilderAsset,BuilderIcon};
  public static void BuilderCodexBatch(){
   GameObject builder=null;
   try{
    Check(!EditorApplication.isPlaying&&Application.unityVersion=="6000.0.75f1","Expected Editor-only Unity 6000.0.75f1");
    PlayerSettings.colorSpace=ColorSpace.Linear;string output=Arg("-rsnOutput");Directory.CreateDirectory(output);
    builder=BuildBuilderCodex(output);
    if(iconDonors){iconDonors.Unload(true);iconDonors=null;}
    var names=PreparedAssetNames();foreach(string n in names)Check(AssetDatabase.LoadMainAssetAtPath(n),"Prepared asset missing: "+n);
    Check(BuildPipeline.BuildAssetBundles(output,new[]{new AssetBundleBuild{assetBundleName="rsn_core_windows",assetNames=names}},BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode,BuildTarget.StandaloneWindows64),"Builder bundle build failed");
    var bundle=AssetBundle.LoadFromFile(Path.Combine(output,"rsn_core_windows"));Check(bundle,"Builder bundle reopen failed");
    try{foreach(string n in names)Check(bundle.LoadAsset<UnityEngine.Object>(n),"Bundle asset missing: "+n);ValidateBuilderBundle(bundle,output);}finally{bundle.Unload(true);}
    File.Copy(Root+"/RSN_CoreIcon.png",Path.Combine(output,"icon.png"),true);Debug.Log("RSN_BUILDER_CODEX_BUILD_SUCCESS");EditorApplication.Exit(0);
   }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
   finally{if(builder)UnityEngine.Object.DestroyImmediate(builder);if(iconDonors)iconDonors.Unload(true);}
  }
 }
}
