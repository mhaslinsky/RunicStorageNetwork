using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace RunicStorage.Build {
public static partial class BuildAssets {
 public static void PreviewTerminal(){
  GameObject model=null;
  try {
   Check(!EditorApplication.isPlaying,"Play Mode prohibited");
   string output=Arg("-rsnOutput");Directory.CreateDirectory(output);
   model=BuildTerminal(output);
   TerminalView(model,output+"/01-overall.png",new Vector3(3,2.3f,-5),false);
   TerminalView(model,output+"/02-book.png",new Vector3(2,3.6f,-5),true);
   TerminalView(model,output+"/03-back.png",new Vector3(-3,2.3f,5),false);
   TerminalView(model,output+"/04-pedestal-rune.png",new Vector3(1.7f,.9f,-4),false,true);
   TerminalView(model,output+"/05-cover.png",new Vector3(4,1f,-3),true);
   TerminalView(model,output+"/06-cover-material.png",new Vector3(3,2.3f,-5),true,false,true);
   Debug.Log("RSN_TERMINAL_PREVIEW_SUCCESS");EditorApplication.Exit(0);
  }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
  finally{if(model)UnityEngine.Object.DestroyImmediate(model);}
 }
 static void TerminalView(GameObject model,string path,Vector3 direction,bool close,bool pedestal=false,bool coverOnly=false,float? framing=null,bool ground=false,float keyYaw=15){
  var scene=EditorSceneManager.NewPreviewScene();var copy=UnityEngine.Object.Instantiate(model);
  UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(copy,scene);
  ApplyIconMaterials(copy);
  var renderers=copy.GetComponentsInChildren<MeshRenderer>();
  if(coverOnly)foreach(var renderer in renderers)renderer.enabled=renderer.name=="RST_Leather"||renderer.name=="RST_Silver";
  var targets=pedestal?renderers.Where(r=>r.name=="RST_BaseRune").ToArray():close?renderers.Where(r=>r.name.Contains("Book")||r.name.Contains("Parchment")||r.name.Contains("Page")||r.name.Contains("Leather")||r.name.Contains("Silver")).ToArray():renderers;
  var bounds=targets[0].bounds;foreach(var r in targets)bounds.Encapsulate(r.bounds);
  Material groundMaterial=null;
  if(ground){
   var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Preview ground";
   UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(floor,scene);
   floor.transform.position=new Vector3(bounds.center.x,bounds.min.y-.001f,bounds.center.z);floor.transform.localScale=Vector3.one*.25f;
   groundMaterial=new Material(Shader.Find("Standard"));groundMaterial.color=new Color(.19f,.215f,.23f,1);groundMaterial.SetFloat("_Glossiness",0);
   floor.GetComponent<Renderer>().sharedMaterial=groundMaterial;
  }
  var cameraObj=new GameObject("TerminalPreviewCamera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObj,scene);
  var camera=cameraObj.AddComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;
  camera.backgroundColor=new Color(.19f,.215f,.23f,1);camera.orthographic=true;camera.orthographicSize=framing??(pedestal?.26f:close?.64f:.99f);
  camera.transform.position=bounds.center+direction;camera.transform.LookAt(bounds.center);camera.nearClipPlane=.03f;camera.farClipPlane=50;camera.renderingPath=RenderingPath.Forward;
  var keyObj=new GameObject("Key");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(keyObj,scene);
  var key=keyObj.AddComponent<Light>();key.type=LightType.Directional;key.intensity=1.8f;key.color=new Color(1,.92f,.82f);key.transform.rotation=Quaternion.Euler(45,keyYaw,0);
  key.shadows=LightShadows.Soft;key.shadowStrength=.8f;key.shadowBias=.02f;key.shadowNormalBias=.15f;key.shadowResolution=LightShadowResolution.VeryHigh;key.renderMode=LightRenderMode.ForcePixel;
  var fillObj=new GameObject("Fill");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fillObj,scene);
  var fill=fillObj.AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.25f;fill.color=new Color(.85f,.9f,1);fill.transform.rotation=Quaternion.Euler(25,keyYaw+125,0);
  QualitySettings.shadows=ShadowQuality.All;QualitySettings.shadowResolution=ShadowResolution.VeryHigh;QualitySettings.shadowDistance=20;QualitySettings.shadowCascades=2;
  Shader.SetGlobalVector("_SunDir",-key.transform.forward);Shader.SetGlobalColor("_SunColor",key.color*key.intensity);Shader.SetGlobalColor("_AmbientColor",new Color(.25f,.25f,.25f,1));Shader.SetGlobalFloat("_Wet",0);
  Check(Unsupported.SetOverrideLightingSettings(scene),"Cannot configure preview lighting");
  RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.25f,.25f,.25f,1);
  var ambient=new SphericalHarmonicsL2();ambient.AddAmbientLight(new Color(.14f,.14f,.14f));RenderSettings.ambientProbe=ambient;
  RenderSettings.defaultReflectionMode=DefaultReflectionMode.Custom;RenderSettings.customReflectionTexture=EditorGUIUtility.Load("PrefabMode/DefaultReflectionForPrefabMode.exr") as Cubemap;RenderSettings.reflectionIntensity=.4f;
  const int size=1200;
  var rt=new RenderTexture(size,size,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);var previous=RenderTexture.active;
  var capture=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true);var png=new Texture2D(size,size,TextureFormat.RGBA32,false,false);
  try{
   camera.allowHDR=true;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
   capture.ReadPixels(new Rect(0,0,size,size),0,0);capture.Apply();
   var pixels=capture.GetPixels();for(int i=0;i<pixels.Length;i++){pixels[i]=pixels[i].gamma;pixels[i].a=1;}
   png.SetPixels(pixels);png.Apply();File.WriteAllBytes(path,png.EncodeToPNG());
  }finally{
   Unsupported.RestoreOverrideLightingSettings();camera.targetTexture=null;RenderTexture.active=previous;ReleaseIconMaterials(copy);
   UnityEngine.Object.DestroyImmediate(capture);UnityEngine.Object.DestroyImmediate(png);UnityEngine.Object.DestroyImmediate(rt);EditorSceneManager.ClosePreviewScene(scene);
   if(groundMaterial)UnityEngine.Object.DestroyImmediate(groundMaterial);
  }
 }
}
}
