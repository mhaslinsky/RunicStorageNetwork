using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RunicStorage.Build {
 public static partial class BuildAssets {
  const string GatewayAsset=Root+"/RSN_RunicGateway.prefab",GatewayIcon=Root+"/RSN_GatewayIcon.png";
  static GameObject BuildGateway(string output){
   var go=GatewayAssetBuilder.Build();var renderers=go.GetComponentsInChildren<MeshRenderer>();
   var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
   RenderIcon(go,bounds,GatewayIcon,RenderingPath.Forward,256,2.25f,new Vector3(3,1.75f,-7));
   AssetDatabase.Refresh();var icon=(TextureImporter)AssetImporter.GetAtPath(GatewayIcon);
   icon.textureType=TextureImporterType.Sprite;icon.spriteImportMode=SpriteImportMode.Single;icon.mipmapEnabled=false;icon.alphaIsTransparency=true;icon.textureCompression=TextureImporterCompression.Uncompressed;icon.SaveAndReimport();AssetDatabase.SaveAssets();return go;
  }
  static void ValidateGateway(AssetBundle bundle,GameObject source,string output){
   var prefab=bundle.LoadAsset<GameObject>(GatewayAsset);Check(prefab&&prefab.name=="RSN_RunicGateway"&&bundle.LoadAsset<Sprite>(GatewayIcon),"Gateway bundle assets missing");
   Check(prefab.GetComponentsInChildren<MeshRenderer>().Length==10&&prefab.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3)==9069,"Approved v14 geometry changed");
   Check(prefab.GetComponentsInChildren<Collider>().Length==6,"Gateway collider count");
   Check(prefab.GetComponentsInChildren<MonoBehaviour>(true).Length==0&&prefab.GetComponentsInChildren<Light>(true).Length==0&&prefab.GetComponentsInChildren<Camera>(true).Length==0,"Gateway studio/runtime components in visual prefab");
   foreach(var r in prefab.GetComponentsInChildren<MeshRenderer>()){
    var expected=source.GetComponentsInChildren<MeshRenderer>().Single(x=>x.name==r.name).GetComponent<MeshFilter>().sharedMesh;var actual=r.GetComponent<MeshFilter>().sharedMesh;
    Check(actual.vertices.SequenceEqual(expected.vertices)&&actual.normals.SequenceEqual(expected.normals)&&actual.triangles.SequenceEqual(expected.triangles)&&actual.uv.SequenceEqual(expected.uv),"Gateway bundle surface changed: "+r.name);
    Check(r.sharedMaterial&&r.sharedMaterial.shader&&r.sharedMaterial.shader.name!="Custom/Piece"&&!ShaderUtil.ShaderHasError(r.sharedMaterial.shader),"Gateway missing or bundled native material");
   }
   var ghost=UnityEngine.Object.Instantiate(prefab);int checks=0;
   try{
    foreach(var point in new[]{Vector3.zero,new Vector3(160,58,-96),new Vector3(-2030,87,5000)})foreach(float yaw in new[]{0f,37f,135f,270f}){
     var result=TerminalPlacementPosition(ghost,point,Vector3.up,Quaternion.Euler(0,yaw,0),out int anchors);
     Check(anchors==1&&Vector3.Distance(result,point)<.02f,"Gateway placement anchor drift");checks++;
    }
    ghost.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);Physics.SyncTransforms();
    // Physical surfaces are clickable, while the gap around the crystal stays empty.
    foreach(string slot in new[]{"RG_Stone","RG_Timber","RG_Iron","RG_Silver","RG_Core"})Check(ghost.GetComponentsInChildren<MeshCollider>().Any(c=>c.name==slot&&c.sharedMesh),"Missing gateway surface collision: "+slot);
    var gap=new Ray(new Vector3(.38f,1.85f,-2),Vector3.forward);
    Check(!ghost.GetComponentsInChildren<Collider>().Any(c=>c.Raycast(gap,out _,4)),"Gateway air gap blocked by collision");checks++;
   }finally{UnityEngine.Object.DestroyImmediate(ghost);}
   File.WriteAllText(Path.Combine(output,"GatewayAssetReport.json"),"{\"approvedAppearance\":\"v14 ordinary stone\",\"triangles\":9069,\"renderers\":10,\"colliders\":6,\"placementChecks\":"+checks+",\"bundleReload\":true,\"nativeGameAssetsBundled\":false}");
   var b=prefab.GetComponentsInChildren<Renderer>().Select(r=>r.bounds).Aggregate((a,c)=>{a.Encapsulate(c);return a;});
   RenderIcon(prefab,b,Path.Combine(output,"gateway-preview.png"),RenderingPath.Forward,768,2.25f,new Vector3(3,1.75f,-7));
  }
  // Rebuild only the newly approved asset; retain the prepared core/relay/codex
  // assets in this isolated project. The bundle still contains every mod asset.
  public static void GatewayBatch(){
   GameObject gateway=null;
   try{
    Check(!EditorApplication.isPlaying&&Application.unityVersion=="6000.0.75f1","Expected Editor-only Unity 6000.0.75f1");
    PlayerSettings.colorSpace=ColorSpace.Linear;string output=Arg("-rsnOutput");Directory.CreateDirectory(output);
    gateway=BuildGateway(output);
    if(iconDonors){iconDonors.Unload(true);iconDonors=null;}
    var names=new[]{Root+"/RSN_NetworkCore.prefab",Root+"/RSN_CoreIcon.png",Root+"/RSN_RunicRelay.prefab",Root+"/RSN_RelayIcon.png",TerminalAsset,TerminalIcon,CodexAsset,CodexIcon,GatewayAsset,GatewayIcon};
    foreach(string name in names)Check(AssetDatabase.LoadMainAssetAtPath(name),"Prepared asset missing: "+name);
    Check(BuildPipeline.BuildAssetBundles(output,new[]{new AssetBundleBuild{assetBundleName="rsn_core_windows",assetNames=names}},BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode,BuildTarget.StandaloneWindows64),"Gateway bundle build failed");
    var bundle=AssetBundle.LoadFromFile(Path.Combine(output,"rsn_core_windows"));Check(bundle,"Bundle reopen failed");
    try{foreach(string name in names)Check(bundle.LoadAsset<UnityEngine.Object>(name),"Bundle asset missing: "+name);ValidateGateway(bundle,gateway,output);}finally{bundle.Unload(true);}
    File.Copy(Root+"/RSN_CoreIcon.png",Path.Combine(output,"icon.png"),true);Debug.Log("RSN_GATEWAY_BUILD_SUCCESS");EditorApplication.Exit(0);
   }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
   finally{if(gateway)UnityEngine.Object.DestroyImmediate(gateway);if(iconDonors)iconDonors.Unload(true);}
  }
 }
}
