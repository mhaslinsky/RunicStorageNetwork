using System;
using System.Collections.Generic;
using UnityEngine;

namespace RunicStorageNetwork {
 // Shared by runtime and the Editor icon pass. Native assets are never bundled.
 internal static class TerminalMaterials {
  const string Prefix="RSN_Vanilla_";
  static readonly string[] Plain={"RST_Silver","RST_Leather","RST_Parchment","RST_PageEdges","RST_Cloth","RST_Cloth_RedBorder","RST_BannerSymbol"};
  // The inventory book shares the stand's palette without renaming either model's slots.
  static string StyleSlot(string slot){
   switch(slot){
    case "RBC_Leather":
    case "RBC_ClaspLeather":
    case "RBC_HarnessLeather":
    case "RC_Leather":return "RST_Leather";
    case "RC_ClaspLeather":return "RST_Leather";
    case "RBC_Silver":case "RC_Silver":return "RST_Silver";
    case "RBC_Parchment":case "RC_Parchment":return "RST_Parchment";
    case "RBC_Cloth":case "RC_Cloth":return "RST_Cloth";
    default:return slot;
   }
  }
  internal static void Apply(GameObject prefab,Func<string,GameObject> resolve){
   var pending=new List<KeyValuePair<MeshRenderer,Material>>();
   var textures=new List<Texture2D>();
   try{
    foreach(var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true)){
     var original=renderer.sharedMaterial;
     if(!original)throw new InvalidOperationException("Terminal material missing");
     string slot=original.name;if(slot.StartsWith(Prefix,StringComparison.Ordinal))continue;
     string style=StyleSlot(slot);
     bool stone=style=="RST_Stone",wood=style=="RST_Timber",iron=style=="RST_Iron",plain=Array.IndexOf(Plain,style)>=0;
     if(!stone&&!wood&&!iron&&!plain)continue; // Geometric runes and the clasp crystal retain independent emission.
     string donor=stone?"stone_wall_2x1":iron?"iron_floor_1x1":"wood_door";
     string name=stone?"stone_mat":iron?"metalwall":"door_wood";
     var sourcePrefab=resolve(donor);if(!sourcePrefab)throw new InvalidOperationException("Terminal donor missing: "+donor);
     Material source=null;
     foreach(var r in sourcePrefab.GetComponentsInChildren<Renderer>(true))foreach(var m in r.sharedMaterials)
      if(m&&m.name==name&&m.shader&&m.shader.name=="Custom/Piece")source=m;
     if(!source)throw new InvalidOperationException("Terminal donor material missing: "+name);
     var mat=new Material(source){name=Prefix+slot};pending.Add(new KeyValuePair<MeshRenderer,Material>(renderer,mat));
     mat.SetFloat("_Cull",0);mat.SetFloat("_TwoSidedNormals",1);mat.SetFloat("_Cutoff",0);
     mat.SetFloat("_TriplanarMap",iron?1:0);mat.SetFloat("_TriplanarLocalPos",1);mat.SetFloat("_TriplanarScale",1);
     mat.SetFloat("_RippleDistance",0);mat.SetFloat("_MoveableObject",1);mat.SetFloat("_ValueNoise",0);mat.SetFloat("_AddRain",0);
     foreach(string property in new[]{"_MainTex","_BumpMap"}){mat.SetTextureScale(property,Vector2.one);mat.SetTextureOffset(property,Vector2.zero);}
     mat.SetVector("_MainTex_ST",new Vector4(1,1,0,0));
     if(stone||wood)mat.SetFloat("_BumpScale",stone?.25f:.35f);
     if(plain){
      // A neutral native building material, not an unrelated item/vegetation atlas.
      // Preserve authored blue-grey cloth, red trim, leather, paper and silver.
      foreach(string property in mat.GetTexturePropertyNames())mat.SetTexture(property,null);
      mat.SetTexture("_MainTex",Texture2D.whiteTexture);
      // Explicit masks avoid inheriting the donor's textured metal response.
      bool metal=style=="RST_Silver";
      mat.SetTexture("_MetallicTex",metal?Texture2D.whiteTexture:Texture2D.blackTexture);
      mat.SetFloat("_BumpScale",0);mat.SetFloat("_Metallic",original.GetFloat("_Metallic"));
      mat.SetFloat("_Glossiness",original.GetFloat("_Smoothness"));mat.SetFloat("_MetallicAlphaGloss",metal?original.GetFloat("_Smoothness"):0);
      mat.SetVector("_Color",original.GetVector("_BaseLinear"));
      if(metal)mat.SetVector("_MetalColor",original.GetVector("_BaseLinear"));
      if(style=="RST_Leather"){
       // Dark brown leather keeps the cover and spine distinct from silver trim.
       // Use albedo for the leather tone; _Color also serves placement/support tint.
       var albedo=new Texture2D(1,1,TextureFormat.RGBA32,false,true){name=Prefix+slot+"_Albedo"};textures.Add(albedo);
       bool clasp=slot=="RC_ClaspLeather"||slot=="RBC_ClaspLeather";
       albedo.SetPixel(0,0,clasp?new Color(.065f,.032f,.014f,1):slot=="RBC_HarnessLeather"?new Color(.035f,.019f,.009f,1):new Color(.014f,.008f,.004f,1));albedo.Apply(false,true);mat.SetTexture("_MainTex",albedo);
       mat.SetColor("_Color",Color.white);
       mat.SetFloat("_Metallic",0);mat.SetFloat("_Glossiness",.1f);
      }
      mat.SetColor("_EmissionColor",Color.clear);mat.SetColor("_Emissive",Color.clear);
     }
    }
   }catch{foreach(var pair in pending)Destroy(pair.Value);foreach(var texture in textures)Destroy(texture);throw;}
   foreach(var pair in pending)pair.Key.sharedMaterial=pair.Value;
  }
  internal static void ReleasePreview(GameObject model){
   foreach(var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
    if(renderer.sharedMaterial&&OwnsPreview(renderer.sharedMaterial)){
     var albedo=renderer.sharedMaterial.GetTexture("_MainTex");
     if(albedo&&OwnsPreview(albedo))Destroy(albedo);
     Destroy(renderer.sharedMaterial);
    }
  }
  static bool OwnsPreview(UnityEngine.Object asset){return asset.name.StartsWith(Prefix+"RST_",StringComparison.Ordinal)||asset.name.StartsWith(Prefix+"RC_",StringComparison.Ordinal)||asset.name.StartsWith(Prefix+"RBC_",StringComparison.Ordinal);}
  static void Destroy(UnityEngine.Object asset){if(Application.isPlaying)UnityEngine.Object.Destroy(asset);else UnityEngine.Object.DestroyImmediate(asset);}
 }
}
