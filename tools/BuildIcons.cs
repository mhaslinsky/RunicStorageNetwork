using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace RunicStorage.Build {
public static partial class BuildAssets {
 // Native assets are read only for the Editor photograph. None are saved as
 // project assets or referenced by our prefabs / outgoing AssetBundle.
 static AssetBundle iconDonors;
 public static void PreviewIcons() {
  try {
   foreach(string piece in new[]{"RSN_NetworkCore","RSN_RunicRelay"}) {
    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/"+piece+".prefab");
    var rs=prefab.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
    RenderIcon(prefab,b,Path.Combine(Arg("-rsnOutput"),piece+".png"),RenderingPath.Forward,256,piece.Contains("Relay")?1.65f:2.6f);
    RenderIcon(prefab,b,Path.Combine(Arg("-rsnOutput"),piece+"-large.png"),RenderingPath.Forward,768,piece.Contains("Relay")?1.65f:2.6f);
   }
   Debug.Log("RSN_ICON_PREVIEW_SUCCESS");EditorApplication.Exit(0);
  }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
 }
 static void ApplyIconMaterials(GameObject model) {
  QualitySettings.streamingMipmapsActive=false;
  Texture.streamingTextureForceLoadAll=true;
  if(!iconDonors) iconDonors=AssetBundle.LoadFromFile(@"E:\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles\c4210710");
  Check(iconDonors,"Native icon material bundle unavailable");
  if(model.name.StartsWith("RSN_RunicGateway",StringComparison.Ordinal)){
   RunicStorageNetwork.GatewayMaterials.Apply(model,id=>iconDonors.LoadAsset<GameObject>((id=="YggdrasilWood"?"Assets/GameElements/Items/materials/":"Assets/GameElements/Pieces/")+id+".prefab"));return;
  }
  if(model.name.StartsWith("RSN_RunicStorageTerminal",StringComparison.Ordinal)||model.name.StartsWith("RSN_RunicCodex",StringComparison.Ordinal)||model.name.StartsWith("RSN_RunicBuilderCodex",StringComparison.Ordinal)){
   RunicStorageNetwork.TerminalMaterials.Apply(model,id=>iconDonors.LoadAsset<GameObject>("Assets/GameElements/Pieces/"+id+".prefab"));
   return;
  }
  bool relay=model.name.Contains("Relay");
  foreach(var renderer in model.GetComponentsInChildren<MeshRenderer>()) {
   string slot=renderer.sharedMaterial.name;
   bool stone=slot.EndsWith("Stone"),wood=slot.EndsWith("Timber"),iron=slot.EndsWith("Iron"),plinth=slot.EndsWith("Plinth");
   if(!stone&&!wood&&!iron&&!plinth)continue;
   string donor=wood?"wood_door":iron?"iron_floor_1x1":"stone_wall_2x1";
   string name=wood?"door_wood":iron?"metalwall":"stone_mat";
   var prefab=iconDonors.LoadAsset<GameObject>("Assets/GameElements/Pieces/"+donor+".prefab");
   Check(prefab,"Icon donor missing: "+donor);
   var source=prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).FirstOrDefault(m=>m&&m.name==name&&m.shader&&m.shader.name=="Custom/Piece");
   Check(source,"Native icon material unavailable: "+name);
   var mat=new Material(source){name="IconOnly_"+slot};
   mat.SetFloat("_TriplanarMap",iron?1:0);mat.SetFloat("_TriplanarLocalPos",1);mat.SetFloat("_TriplanarScale",1);
   mat.SetFloat("_Cull",0);mat.SetFloat("_TwoSidedNormals",1);mat.SetFloat("_Cutoff",0);
   if(!iron){
    mat.SetFloat("_RippleDistance",0);mat.SetFloat("_MoveableObject",1);
    foreach(string texture in new[]{"_MainTex","_BumpMap"}){mat.SetTextureScale(texture,Vector2.one);mat.SetTextureOffset(texture,Vector2.zero);}
    mat.SetVector("_MainTex_ST",new Vector4(1,1,0,0));
    mat.SetFloat("_BumpScale",plinth?0:(relay?(stone?.55f:.65f):(stone?1.15f:1.25f)));
    if(plinth){mat.SetTexture("_BumpMap",null);mat.SetFloat("_ValueNoise",0);}
   }
   Check(mat.shader.isSupported,"Native icon shader unsupported");
   renderer.sharedMaterial=mat;
   foreach(string property in new[]{"_MainTex","_BumpMap","_MetallicTex"}) {
    var texture=mat.GetTexture(property) as Texture2D;
    Debug.Log("RSN_ICON_TEXTURE "+slot+" "+property+"="+(texture?texture.name+" "+texture.width+"x"+texture.height+" loadedMip="+texture.loadedMipmapLevel:"none"));
    if(property=="_MainTex")Check(texture,"Icon albedo texture missing: "+slot);
   }
   Debug.Log("RSN_ICON_MATERIAL "+slot+" <- "+donor+"/"+name);
  }
 }
 static void ReleaseIconMaterials(GameObject model) {
  RunicStorageNetwork.GatewayMaterials.ReleasePreview(model);
  RunicStorageNetwork.TerminalMaterials.ReleasePreview(model);
  foreach(var r in model.GetComponentsInChildren<MeshRenderer>())if(r.sharedMaterial&&r.sharedMaterial.name.StartsWith("IconOnly_"))UnityEngine.Object.DestroyImmediate(r.sharedMaterial);
 }
 static void RenderIcon(GameObject model,Bounds b,string path,RenderingPath renderingPath=RenderingPath.Forward,int size=256,float framing=2.6f,Vector3? direction=null) {
  var scene=EditorSceneManager.NewPreviewScene(); var copy=UnityEngine.Object.Instantiate(model);
  UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(copy,scene);
  ApplyIconMaterials(copy);
  var cameraObj=new GameObject("IconCamera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObj,scene);
  var camera=cameraObj.AddComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;
  camera.renderingPath=renderingPath;
  camera.backgroundColor=Color.clear;camera.orthographic=true;camera.orthographicSize=framing*.88f;
  camera.transform.position=b.center+(direction??new Vector3(5,2,-7));camera.transform.LookAt(b.center);camera.nearClipPlane=0.1f;camera.farClipPlane=50;
  var lightObj=new GameObject("IconLight");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObj,scene);
  var light=lightObj.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;light.color=new Color(1f,.92f,.82f);light.transform.rotation=Quaternion.Euler(45,15f,0);
  var fillObj=new GameObject("IconFill");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fillObj,scene);
  var fill=fillObj.AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.25f;fill.color=new Color(.85f,.9f,1f);fill.transform.rotation=Quaternion.Euler(25,140,0);
  light.shadows=LightShadows.Soft;light.shadowStrength=.8f;light.shadowBias=.02f;light.shadowNormalBias=.15f;light.shadowResolution=LightShadowResolution.VeryHigh;light.renderMode=LightRenderMode.ForcePixel;
  QualitySettings.shadows=ShadowQuality.All;QualitySettings.shadowResolution=ShadowResolution.VeryHigh;QualitySettings.shadowDistance=20;QualitySettings.shadowCascades=2;
  foreach(var renderer in copy.GetComponentsInChildren<Renderer>()){renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;}
  Shader.SetGlobalVector("_SunDir",-light.transform.forward);
  Shader.SetGlobalColor("_SunColor",light.color*light.intensity);
  Shader.SetGlobalColor("_AmbientColor",new Color(.25f,.25f,.25f,1));
  Shader.SetGlobalFloat("_Wet",0);
  Check(Unsupported.SetOverrideLightingSettings(scene),"Cannot configure icon preview lighting");
  RenderSettings.ambientMode=AmbientMode.Flat;
  RenderSettings.ambientLight=new Color(.25f,.25f,.25f,1);
  var ambient=new SphericalHarmonicsL2();ambient.AddAmbientLight(new Color(.14f,.14f,.14f));RenderSettings.ambientProbe=ambient;
  RenderSettings.defaultReflectionMode=DefaultReflectionMode.Custom;
  RenderSettings.customReflectionTexture=EditorGUIUtility.Load("PrefabMode/DefaultReflectionForPrefabMode.exr") as Cubemap;
  RenderSettings.reflectionIntensity=.4f;
  // Supersample the final PNG: native point-filtered textures and normal-map
  // highlights otherwise alias into isolated white pixels at menu-icon scale.
  int samples=size<=256?3:1;int renderSize=size*samples;
  var rt=new RenderTexture(renderSize,renderSize,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);var previous=RenderTexture.active;
  var tex=new Texture2D(renderSize,renderSize,TextureFormat.RGBAFloat,false,true);
  var maskTexture=new Texture2D(renderSize,renderSize,TextureFormat.RGBA32,false,true);
  var maskTarget=new RenderTexture(renderSize,renderSize,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
  try {
   camera.allowHDR=true;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
   tex.ReadPixels(new Rect(0,0,renderSize,renderSize),0,0);tex.Apply();
   var linear=tex.GetPixels();var pixels=new Color[size*size];
   var maskShader=AssetDatabase.LoadAssetAtPath<Shader>(Root+"/Editor/IconSilhouette.shader");
   Check(maskShader&&!ShaderUtil.ShaderHasError(maskShader),"Icon silhouette shader missing");
   camera.allowHDR=false;camera.targetTexture=maskTarget;camera.RenderWithShader(maskShader,"");
   RenderTexture.active=maskTarget;maskTexture.ReadPixels(new Rect(0,0,renderSize,renderSize),0,0);maskTexture.Apply();
   var mask=maskTexture.GetPixels();int visible=0;
   for(int y=0;y<size;y++)for(int x=0;x<size;x++) {
    Color sum=Color.clear;float coverage=0;
    for(int j=0;j<samples;j++)for(int k=0;k<samples;k++){
     int index=(y*samples+j)*renderSize+x*samples+k;
     float alpha=mask[index].r;sum+=linear[index]*alpha;coverage+=alpha;
    }
    // Preserve straight alpha; averaging black background into RGB produces halos.
    var color=coverage>0?(sum/coverage).gamma:Color.clear;color.a=coverage/(samples*samples);pixels[y*size+x]=color;if(coverage>0)visible++;
   }
   Check(visible>size*size*.15f&&visible<size*size*.85f,"Invalid icon silhouette coverage");
   Debug.Log("RSN_ICON_COVERAGE "+Path.GetFileName(path)+" visible="+visible+" total="+pixels.Length);
   var png=new Texture2D(size,size,TextureFormat.RGBA32,false,false);
   try{png.SetPixels(pixels);png.Apply();File.WriteAllBytes(path,png.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(png);}
  }
  finally {Unsupported.RestoreOverrideLightingSettings();camera.targetTexture=null;RenderTexture.active=previous;ReleaseIconMaterials(copy);UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(maskTexture);UnityEngine.Object.DestroyImmediate(maskTarget);UnityEngine.Object.DestroyImmediate(rt);EditorSceneManager.ClosePreviewScene(scene);}
 }
}
}
