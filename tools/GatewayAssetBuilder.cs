using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RunicStorage.Build {
 public static class GatewayAssetBuilder {
  const string Root="Assets/RunicStorageGame/Gateway";
  const string Source=Root+"/Source";

  [Serializable] class Materials { public Record[] materials; }
  [Serializable] class Record {
   public string name; public string[] objects; public float[] base_color,emission_color;
   public float metallic,roughness,emission_strength_unlinked_default; public bool backface_culling;
  }
  [Serializable] class Report {
   public string source="RunicGateway/export_v10",unityVersion,previewOnly="Editor rendering; no mod registration or game launch";
   public int renderers,triangles;public Vector3 size;
   public bool preservedGeometryAndNormals,crystalUVPreserved,engravingPreserved=true,nativeAssetsPersisted=false;
   public string stoneMaterial="stone_wall_2x1 / stone_mat";
   public float stoneNormalStrength=.40f;
   public bool stoneSurfaceRefined=true,otherGeometryPreserved=true;
   public GatewayStoneFinish.Result stoneFinish;
   public List<string> bindings=new List<string>();
   public List<string> meshes=new List<string>();
  }
  static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
  static Vector4 Vec(float[] a){return new Vector4(a[0],a[1],a[2],a[3]);}
  static Report Results;
  public static GameObject Build(){
   if(EditorApplication.isPlaying)throw new InvalidOperationException("Editor-only asset build");
   Directory.CreateDirectory(Root+"/Materials");Directory.CreateDirectory(Root+"/Meshes");AssetDatabase.Refresh();Results=new Report();
   var go=Import();
   foreach(var renderer in go.GetComponentsInChildren<MeshRenderer>())if(new[]{"RG_Stone","RG_Timber","RG_Iron","RG_Silver","RG_Core"}.Contains(renderer.name)){
    var collider=renderer.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=renderer.GetComponent<MeshFilter>().sharedMesh;collider.convex=false;
   }
   var anchor=new GameObject("PlacementAnchor");anchor.transform.SetParent(go.transform,false);
   var box=anchor.AddComponent<BoxCollider>();box.center=new Vector3(0,.025f,0);box.size=new Vector3(2.64f,.05f,.65f);
   PrefabUtility.SaveAsPrefabAsset(go,"Assets/RunicStorageGame/RSN_RunicGateway.prefab");AssetDatabase.SaveAssets();return go;
  }
  static GameObject Import(){
   var records=JsonUtility.FromJson<Materials>(File.ReadAllText(Source+"/materials.json")).materials;
   Check(records.Length==10,"Expected v10 material slots");
   var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RunicStorageGame/CoreRuntime.shader");
   Check(shader&&!ShaderUtil.ShaderHasError(shader),"Missing source-color shader");
   var maskPath=Source+"/RG_Core_EmissionMask_128.png";
   var maskImporter=(TextureImporter)AssetImporter.GetAtPath(maskPath);
   maskImporter.sRGBTexture=false;maskImporter.mipmapEnabled=false;maskImporter.textureCompression=TextureImporterCompression.Uncompressed;
   maskImporter.filterMode=FilterMode.Point;maskImporter.wrapMode=TextureWrapMode.Clamp;maskImporter.SaveAndReimport();
   var mask=AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
   var mats=new Dictionary<string,Material>();
   foreach(var record in records){
    string slot=record.objects.Single(),path=Root+"/Materials/"+slot+".mat";
    var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
    if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}
    mat.shader=shader;mat.name=slot;mat.SetVector("_BaseLinear",Vec(record.base_color));
    mat.SetVector("_EmissionLinear",Vec(record.emission_color));mat.SetFloat("_EmissionStrength",record.emission_strength_unlinked_default);
    mat.SetFloat("_Metallic",record.metallic);mat.SetFloat("_Smoothness",1-record.roughness);mat.SetFloat("_Cull",0);
    mat.SetColor("_Color",Color.white);mat.SetColor("_EmissionColor",Color.clear);
    mat.SetFloat("_UseFacetMask",slot=="RG_Core"?1:0);mat.SetFloat("_MaskDecode",1);mat.SetTexture("_FacetMask",mask);
    EditorUtility.SetDirty(mat);mats.Add(slot,mat);
   }
   string fbx=Source+"/RunicGateway.fbx";
   var importer=(ModelImporter)AssetImporter.GetAtPath(fbx);Check(importer,"Missing Gateway FBX");
   importer.globalScale=1;importer.useFileScale=true;importer.bakeAxisConversion=true;
   importer.importNormals=ModelImporterNormals.Import;importer.importTangents=ModelImporterTangents.None;
   importer.meshCompression=ModelImporterMeshCompression.Off;importer.weldVertices=false;importer.optimizeMeshPolygons=false;importer.optimizeMeshVertices=false;
   importer.isReadable=true;importer.generateSecondaryUV=false;importer.preserveHierarchy=true;importer.addCollider=false;
   importer.importCameras=false;importer.importLights=false;importer.importAnimation=false;importer.animationType=ModelImporterAnimationType.None;
   importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
   foreach(var record in records)importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),record.name),mats[record.objects[0]]);
   importer.SaveAndReimport();
   var root=new GameObject("RSN_RunicGateway");
   var source=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));source.transform.SetParent(root.transform,false);
   PrefabUtility.UnpackPrefabInstance(source,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
   Check(root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3)==6163,"Source v10 triangle count changed");
   Results.stoneFinish=GatewayStoneFinish.Apply(root);
   var renderers=root.GetComponentsInChildren<MeshRenderer>();
   Check(renderers.Length==10,"Unexpected renderer count");var bounds=renderers[0].bounds;
   foreach(var renderer in renderers){
    Check(mats.ContainsKey(renderer.name),"Unexpected renderer "+renderer.name);
    renderer.sharedMaterial=mats[renderer.name];renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
    bounds.Encapsulate(renderer.bounds);
    if(new[]{"RG_Stone","RG_Timber","RG_Iron","RG_Silver","RG_Banners","RG_BannerSymbols"}.Contains(renderer.name))AddUV(renderer);
    var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
    Results.meshes.Add(renderer.name+": "+mesh.vertexCount+" vertices, "+mesh.triangles.Length/3+" triangles, bounds "+renderer.bounds);
   }
   Results.triangles=root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3);Results.renderers=renderers.Length;Results.size=bounds.size;
   Check(Results.triangles==4223+Results.stoneFinish.finishedTriangles,"Unexpected non-stone geometry change");
   Check(Vector3.Distance(bounds.size,new Vector3(3.1f,3.5f,.889746f))<.025f&&Mathf.Abs(bounds.min.y)<.001f,"Unexpected import axes, size or origin");
   Check(root.GetComponentsInChildren<MonoBehaviour>().Length==0&&root.GetComponentsInChildren<Collider>().Length==0,"Preview must remain visual only");
   var importedCore=source.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="RG_Core").sharedMesh;
   Check(importedCore.uv.Length==importedCore.vertexCount,"Crystal facet mask UV missing");
   Results.crystalUVPreserved=true;Results.preservedGeometryAndNormals=false;
   // Only original authored materials are persisted. Native game assets stay in memory.

   return root;
  }
  static void AddUV(MeshRenderer renderer){
   var filter=renderer.GetComponent<MeshFilter>();var source=filter.sharedMesh;
   var p=source.vertices;var n=source.normals;var t=source.triangles;var sourceUV=source.uv;var sourceProjection=source.uv2;
   Check(n.Length==p.Length,"Imported custom normals missing: "+renderer.name);
   var world=p.Select(renderer.transform.TransformPoint).ToArray();
   // Find connected pieces for timber grain; position matching is used only for analysis, never welding.
   var parents=Enumerable.Range(0,p.Length).ToArray();
   Func<int,int> find=null;find=a=>parents[a]==a?a:parents[a]=find(parents[a]);
   Action<int,int> join=(a,b)=>parents[find(a)]=find(b);
   var shared=new Dictionary<string,int>();
   for(int i=0;i<p.Length;i++){
    var v=world[i];string k=Mathf.RoundToInt(v.x*100000)+","+Mathf.RoundToInt(v.y*100000)+","+Mathf.RoundToInt(v.z*100000);
    if(shared.TryGetValue(k,out int other))join(i,other);else shared.Add(k,i);
   }
   for(int i=0;i<t.Length;i+=3){join(t[i],t[i+1]);join(t[i],t[i+2]);}
   var groups=Enumerable.Range(0,p.Length).GroupBy(find).ToArray();
   var axes=new Dictionary<int,Vector3>();
   var blockBounds=new Dictionary<int,Bounds>();
   foreach(var g in groups){
    Vector3 center=g.Select(i=>world[i]).Aggregate(Vector3.zero,(a,b)=>a+b)/g.Count();
    var b=new Bounds(world[g.First()],Vector3.zero);foreach(int i in g)b.Encapsulate(world[i]);
    blockBounds[g.Key]=b;
    Vector3 axis=b.size.y>=b.size.x&&b.size.y>=b.size.z?Vector3.up:b.size.x>=b.size.z?Vector3.right:Vector3.forward;
    for(int pass=0;pass<16;pass++){Vector3 next=Vector3.zero;foreach(int i in g){var d=world[i]-center;next+=d*Vector3.Dot(d,axis);}if(next.sqrMagnitude>1e-12f)axis=next.normalized;}
    if(axis.y<0)axis=-axis;axes[g.Key]=axis;
   }
   var vertices=new Vector3[t.Length];var normals=new Vector3[t.Length];var uv=new Vector2[t.Length];
   for(int j=0;j<t.Length;j+=3){
    Vector3 face=Vector3.Cross(world[t[j+1]]-world[t[j]],world[t[j+2]]-world[t[j]]).normalized;
    Vector3 u,v;
    if(Mathf.Abs(face.z)>=Mathf.Abs(face.x)&&Mathf.Abs(face.z)>=Mathf.Abs(face.y)){u=Vector3.right;v=Vector3.up;}
    else if(Mathf.Abs(face.x)>Mathf.Abs(face.y)){u=Vector3.forward;v=Vector3.up;}
    else {u=Vector3.right;v=Vector3.forward;}
    for(int k=0;k<3;k++){
     int at=j+k,idx=t[at];var point=world[idx];vertices[at]=p[idx];normals[at]=n[idx];
     if(renderer.name=="RG_Stone"&&sourceUV.Length==p.Length){
      // The vanilla stone wall is an atlas, not a repeating marble texture.
      // Map each block inside a verified opaque stone patch; keep the original projection across dressed faces.
      Check(sourceProjection.Length==p.Length,"Missing stone face projection");
      int group=find(idx),projection=Mathf.RoundToInt(sourceProjection[idx].x);var box=blockBounds[group];
      var original=sourceUV[idx]/.65f;
      float width=Mathf.Max(projection==1?box.size.z:box.size.x,.0001f),height=Mathf.Max(projection==2?box.size.z:box.size.y,.0001f);
      float across=Mathf.Clamp(original.x-(projection==1?box.min.z:box.min.x),0,width);
      float up=Mathf.Clamp(original.y-(projection==2?box.min.z:box.min.y),0,height);
      // Isotropic texel scale prevents long, low foundation pieces stretching the atlas into horizontal stripes.
      // This rectangle stays inside the inspected opaque lower-left stone field of the 128x128 atlas.
      float density=Mathf.Min(.45f,.66f/width,.32f/height);
      uv[at]=new Vector2(.025f+(.66f-width*density)*.5f+across*density,.025f+(.32f-height*density)*.5f+up*density);
     } else if(renderer.name=="RG_Timber"){
      var grain=axes[find(idx)];var across=Vector3.Cross(face,grain).normalized;
      uv[at]=Mathf.Abs(Vector3.Dot(face,grain))>.8f?new Vector2(Vector3.Dot(point,u),Vector3.Dot(point,v))*1.6f:new Vector2(Vector3.Dot(point,across)*1.8f,Vector3.Dot(point,grain)*.55f);
     } else if(renderer.name=="RG_Banners"){
      // Sample the unmarked cloth part of the native banner atlas, avoiding its pole and ragged edge.
      float across=Mathf.InverseLerp(.93f,1.4305f,Mathf.Abs(point.x));
      float height=Mathf.InverseLerp(1.5596f,2.59f,point.y);
      uv[at]=new Vector2(Mathf.Lerp(.052f,.18f,across),Mathf.Lerp(.18f,.86f,height));
     } else uv[at]=new Vector2(Vector3.Dot(point,u),Vector3.Dot(point,v))*(renderer.name=="RG_Stone"?.65f:1f);
    }
   }
   var mesh=new Mesh{name=renderer.name+"_Textured_v14"};mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;
   mesh.triangles=Enumerable.Range(0,t.Length).ToArray();mesh.RecalculateBounds();mesh.RecalculateTangents();
   // Compare every triangle corner, including normals. Engraved geometry is not remodeled.
   for(int i=0;i<t.Length;i++)Check(vertices[i]==p[t[i]]&&normals[i]==n[t[i]],"Geometry/normal changed while adding UV");
   Check(mesh.tangents.All(a=>!float.IsNaN(a.x)&&!float.IsNaN(a.y)&&!float.IsNaN(a.z)),"Invalid UV tangents");
   string path=Root+"/Meshes/"+renderer.name+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
   if(saved){
    // Mesh setters invalidate GPU buffers as well as serialized data during repeated preview captures.
    saved.Clear();saved.vertices=mesh.vertices;saved.normals=mesh.normals;saved.uv=mesh.uv;
    saved.triangles=mesh.triangles;saved.tangents=mesh.tangents;saved.RecalculateBounds();
    Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);
   }else{AssetDatabase.CreateAsset(mesh,path);saved=mesh;}
   filter.sharedMesh=saved;
  }
 }
}
