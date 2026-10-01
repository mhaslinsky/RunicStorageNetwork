using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace RunicStorageNetwork {
 // Exact approved v14 material setup, shared between runtime and Editor icons.
 internal static class GatewayMaterials {
  const string Prefix="RSN_Gateway_";
  static Material Donor(Func<string,GameObject> resolve,string id,string name){
   var go=resolve(id);if(!go)throw new InvalidOperationException("Gateway material donor missing: "+id);
   var m=go.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).FirstOrDefault(x=>x&&x.shader&&x.shader.name=="Custom/Piece"&&(name==null||x.name==name));
   if(!m)throw new InvalidOperationException("Gateway material missing: "+id+"/"+name);return m;
  }
  static void Set(Material m,string p,float v){if(m.HasProperty(p))m.SetFloat(p,v);}
  internal static void Apply(GameObject model,Func<string,GameObject> resolve){
   var created=new List<UnityEngine.Object>();var pending=new List<(MeshRenderer,Material)>();
   try {

   var stone=Donor(resolve,"stone_wall_2x1","stone_mat");
   var wood=Donor(resolve,"YggdrasilWood","Shoot_Stack_mat");
   var cloth=Donor(resolve,"piece_banner01",null);
   var iron=Donor(resolve,"iron_floor_1x1","metalwall");var neutral=Donor(resolve,"wood_door","door_wood");
   foreach(var r in model.GetComponentsInChildren<MeshRenderer>()){
    string slot=r.name;var original=r.sharedMaterial;
    if(original.name.StartsWith(Prefix,StringComparison.Ordinal))continue;
    if(new[]{"RG_Core","RG_Runes","RG_BaseRune","RG_EitrChannels"}.Contains(slot))continue;
    Material donor=slot=="RG_Stone"?stone:slot=="RG_Timber"?wood:slot=="RG_Iron"?iron:neutral;
    var mat=new Material(donor){name=Prefix+slot};created.Add(mat);
    Set(mat,"_Cull",0);Set(mat,"_TwoSidedNormals",1);Set(mat,"_Cutoff",0);
    Set(mat,"_RippleDistance",0);Set(mat,"_ValueNoise",0);Set(mat,"_ValueNoiseVertex",0);Set(mat,"_MoveableObject",1);Set(mat,"_AddRain",0);
    Set(mat,"_TriplanarMap",slot=="RG_Iron"?1:0);Set(mat,"_TriplanarLocalPos",1);Set(mat,"_TriplanarScale",1);
    foreach(string name in new[]{"_MainTex","_BumpMap"}){mat.SetTextureScale(name,Vector2.one);mat.SetTextureOffset(name,Vector2.zero);}
    mat.SetVector("_MainTex_ST",new Vector4(1,1,0,0));
    if(slot=="RG_Stone"){
     Set(mat,"_BumpScale",.40f);Set(mat,"_Glossiness",.08f);Set(mat,"_MetallicAlphaGloss",.08f);
    } else if(slot=="RG_Timber"){
     Set(mat,"_BumpScale",.28f);Set(mat,"_Glossiness",.12f);Set(mat,"_Metallic",0);Set(mat,"_MetallicAlphaGloss",0);
     mat.SetVector("_Color",new Vector4(.41f,.42f,.39f,1));
    } else if(slot=="RG_Iron")Set(mat,"_BumpScale",.45f);
    else {
     foreach(string name in mat.GetTexturePropertyNames())mat.SetTexture(name,null);
     bool silver=slot=="RG_Silver";
     var tint=original.GetVector("_BaseLinear");
     var albedo=new Texture2D(1,1,TextureFormat.RGBA32,false,true){name=slot+"_AuthoredLinearAlbedo"};created.Add(albedo);
     albedo.SetPixel(0,0,new Color(tint.x,tint.y,tint.z,1));albedo.Apply(false,true);
     mat.SetTexture("_MainTex",albedo);mat.SetTexture("_MetallicTex",silver?Texture2D.whiteTexture:Texture2D.blackTexture);
     Set(mat,"_Metallic",silver?1:0);Set(mat,"_BumpScale",0);
     Set(mat,"_Glossiness",silver?.68f:.04f);Set(mat,"_MetallicAlphaGloss",silver?.68f:0);
     mat.SetColor("_Color",Color.white);
     if(silver)mat.SetVector("_MetalColor",original.GetVector("_BaseLinear"));
     if(slot=="RG_Banners"){
      // Native cloth normals retain weave, while authored linear albedo keeps the blue-grey palette.
      mat.SetTexture("_BumpMap",cloth.GetTexture("_BumpMap"));Set(mat,"_BumpScale",.20f);
     }
     mat.SetColor("_EmissionColor",Color.clear);if(mat.HasProperty("_Emissive"))mat.SetColor("_Emissive",Color.clear);
    }
    pending.Add((r,mat));
   }

   }catch{foreach(var asset in created)Release(asset);throw;}
   foreach(var pair in pending)pair.Item1.sharedMaterial=pair.Item2;
  }
  internal static void ReleasePreview(GameObject model){
   foreach(var r in model.GetComponentsInChildren<MeshRenderer>(true))if(r.sharedMaterial&&r.sharedMaterial.name.StartsWith(Prefix,StringComparison.Ordinal)){
    var albedo=r.sharedMaterial.GetTexture("_MainTex");if(albedo&&albedo.name.EndsWith("_AuthoredLinearAlbedo",StringComparison.Ordinal))Release(albedo);
    Release(r.sharedMaterial);
   }
  }
  static void Release(UnityEngine.Object o){if(Application.isPlaying)UnityEngine.Object.Destroy(o);else UnityEngine.Object.DestroyImmediate(o);}
 }
}
