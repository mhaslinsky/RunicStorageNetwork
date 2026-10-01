using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RunicStorage.Build {
 // Baked, deterministic stone finishing. No frame-time displacement and no world-position dependency.
 public static class GatewayStoneFinish {
  [Serializable] public sealed class Result {
   public int sourceTriangles,finishedTriangles,protectedCorners,movedCorners;
   public float maxDisplacement;
  }
  struct Face {public Vector3 a,b,c,na,nb,nc;public int projection;}
  static string Key(Vector3 p){return Mathf.RoundToInt(p.x*100000)+","+Mathf.RoundToInt(p.y*100000)+","+Mathf.RoundToInt(p.z*100000);}
  static float Noise(Vector3 p){return (Mathf.PerlinNoise(p.x+31.71f,p.y+p.z*.61f+14.12f)+Mathf.PerlinNoise(p.z+27.31f,p.x+p.y*.43f+7.15f))*.5f;}
  static float Ease(float low,float high,float v){return Mathf.SmoothStep(0,1,Mathf.InverseLerp(low,high,v));}
  static List<Face> Faces(MeshFilter filter){
   var m=filter.sharedMesh;var p=m.vertices.Select(filter.transform.TransformPoint).ToArray();var n=m.normals.Select(filter.transform.TransformDirection).ToArray();var t=m.triangles;
   var result=new List<Face>();for(int i=0;i<t.Length;i+=3){
    var f=new Face{a=p[t[i]],b=p[t[i+1]],c=p[t[i+2]],na=n[t[i]],nb=n[t[i+1]],nc=n[t[i+2]]};
    var face=Vector3.Cross(f.b-f.a,f.c-f.a).normalized;
    f.projection=Mathf.Abs(face.z)>=Mathf.Abs(face.x)&&Mathf.Abs(face.z)>=Mathf.Abs(face.y)?0:Mathf.Abs(face.x)>Mathf.Abs(face.y)?1:2;
    result.Add(f);
   }return result;
  }
  static Face F(Vector3 a,Vector3 b,Vector3 c,Vector3 na,Vector3 nb,Vector3 nc){return new Face{a=a,b=b,c=c,na=na,nb=nb,nc=nc};}
  static List<Face> Split(List<Face> input){
   var output=new List<Face>();const float limit=.25f*.25f;
   foreach(var f in input){
    int begin=output.Count;
    var a=f.a;var b=f.b;var c=f.c;var na=f.na;var nb=f.nb;var nc=f.nc;
    bool ab=(a-b).sqrMagnitude>limit,bc=(b-c).sqrMagnitude>limit,ca=(c-a).sqrMagnitude>limit;
    int count=(ab?1:0)+(bc?1:0)+(ca?1:0);
    if(count==0){output.Add(f);continue;}
    if(count==1){
     if(bc){a=f.b;b=f.c;c=f.a;na=f.nb;nb=f.nc;nc=f.na;}
     else if(ca){a=f.c;b=f.a;c=f.b;na=f.nc;nb=f.na;nc=f.nb;}
     var mid=(a+b)*.5f;var nm=(na+nb).normalized;
     output.Add(F(a,mid,c,na,nm,nc));output.Add(F(mid,b,c,nm,nb,nc));
    } else if(count==2){
     // Rotate until CA is the unsplit edge. Shared midpoint positions match on both adjacent faces.
     if(!ab){a=f.b;b=f.c;c=f.a;na=f.nb;nb=f.nc;nc=f.na;}
     else if(!bc){a=f.c;b=f.a;c=f.b;na=f.nc;nb=f.na;nc=f.nb;}
     var m1=(a+b)*.5f;var m2=(b+c)*.5f;var n1=(na+nb).normalized;var n2=(nb+nc).normalized;
     output.Add(F(m1,b,m2,n1,nb,n2));output.Add(F(a,m1,c,na,n1,nc));output.Add(F(m1,m2,c,n1,n2,nc));
    } else {
     var m1=(a+b)*.5f;var m2=(b+c)*.5f;var m3=(c+a)*.5f;
     var n1=(na+nb).normalized;var n2=(nb+nc).normalized;var n3=(nc+na).normalized;
     output.Add(F(a,m1,m3,na,n1,n3));output.Add(F(m1,b,m2,n1,nb,n2));output.Add(F(m3,m2,c,n3,n2,nc));output.Add(F(m1,m2,m3,n1,n2,n3));
    }
    for(int i=begin;i<output.Count;i++){var child=output[i];child.projection=f.projection;output[i]=child;}
   }
   return output;
  }
  static Vector3 Closest(Vector3 p,Face f){
   var ab=f.b-f.a;var ac=f.c-f.a;var ap=p-f.a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
   if(d1<=0&&d2<=0)return f.a;
   var bp=p-f.b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);if(d3>=0&&d4<=d3)return f.b;
   float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return f.a+ab*(d1/(d1-d3));
   var cp=p-f.c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);if(d6>=0&&d5<=d6)return f.c;
   float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return f.a+ac*(d2/(d2-d6));
   float va=d3*d6-d5*d4;if(va<=0&&(d4-d3)>=0&&(d5-d6)>=0)return f.b+(f.c-f.b)*((d4-d3)/((d4-d3)+(d5-d6)));
   float denom=va+vb+vc;if(Mathf.Abs(denom)<1e-15f)return f.a;
   return f.a+(ab*vb+ac*vc)/denom;
  }
  static float Distance(Vector3 p,List<Face> faces,float limit){
   float squared=limit*limit;foreach(var f in faces){
    var bounds=new Bounds(f.a,Vector3.zero);bounds.Encapsulate(f.b);bounds.Encapsulate(f.c);
    if(bounds.SqrDistance(p)>squared)continue;
    squared=Mathf.Min(squared,(p-Closest(p,f)).sqrMagnitude);
   }return Mathf.Sqrt(squared);
  }
  public static Result Apply(GameObject model){
   var filter=model.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="RG_Stone");
   var original=Faces(filter);var result=new Result{sourceTriangles=original.Count};
   var protectedFaces=model.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="RG_Runes"||f.name=="RG_BaseRune"||f.name=="RG_EitrChannels").SelectMany(Faces).ToList();
   var fittings=model.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="RG_Iron"||f.name=="RG_Silver"||f.name=="RG_Timber").SelectMany(Faces).ToList();
   var faces=original;for(int pass=0;pass<4;pass++){
    var next=Split(faces);if(next.Count==faces.Count||next.Count>5500)break;faces=next;
   }
   if(faces.Count>5500)throw new InvalidOperationException("Stone refinement exceeded preview polygon budget");
   // A continuous displacement field keeps adjacent bevel faces joined, even across hard normals.
   var deltas=new Dictionary<string,Vector3>();var pin=new HashSet<string>();
   foreach(var f in faces)foreach(var p in new[]{f.a,f.b,f.c}){
    string k=Key(p);if(deltas.ContainsKey(k))continue;
    float runeDistance=Distance(p,protectedFaces,.20f),fittingDistance=Distance(p,fittings,.045f);
    float mask=Ease(.075f,.16f,runeDistance)*Ease(.008f,.035f,fittingDistance)*Ease(.012f,.07f,p.y);
    if(mask==0)pin.Add(k);
    // Broad dressing marks and shallow chipped edges: bounded to 9 mm, with finer relief left to the material.
    var q=p*4.7f;
    var delta=new Vector3(Noise(q)-.5f,Noise(q+new Vector3(7,19,31))-.5f,Noise(q+new Vector3(43,11,17))-.5f)*.018f;
    var fine=p*17.5f;
    delta+=new Vector3(Noise(fine)-.5f,Noise(fine+new Vector3(9,21,2))-.5f,Noise(fine+new Vector3(4,32,8))-.5f)*.006f;
    delta=Vector3.ClampMagnitude(delta*mask,.009f);deltas.Add(k,delta);
   }
   // Tiny source bevel faces can be fragile; locally relax their shared displacement until winding is safe.
   for(int pass=0;pass<20;pass++){
    var relax=new HashSet<string>();
    foreach(var f in faces){
     var oldN=Vector3.Cross(f.b-f.a,f.c-f.a);if(oldN.sqrMagnitude<=1e-14f)continue;
     var a=f.a+deltas[Key(f.a)];var b=f.b+deltas[Key(f.b)];var c=f.c+deltas[Key(f.c)];var next=Vector3.Cross(b-a,c-a);
     if(next.sqrMagnitude<1e-14f||Vector3.Dot(oldN,next)<=0){relax.Add(Key(f.a));relax.Add(Key(f.b));relax.Add(Key(f.c));}
    }
    if(relax.Count==0)break;foreach(string k in relax)deltas[k]=pass<15?deltas[k]*.25f:Vector3.zero;
   }
   result.maxDisplacement=deltas.Values.Max(v=>v.magnitude);
   var vertices=new List<Vector3>();var outputNormals=new List<Vector3>();var uv=new List<Vector2>();var projectionIds=new List<Vector2>();
   foreach(var f in faces){
    var a=f.a+deltas[Key(f.a)];var b=f.b+deltas[Key(f.b)];var c=f.c+deltas[Key(f.c)];
    var oldN=Vector3.Cross(f.b-f.a,f.c-f.a);var newN=Vector3.Cross(b-a,c-a);
    if(oldN.sqrMagnitude>1e-14f&&(newN.sqrMagnitude<1e-14f||Vector3.Dot(oldN,newN)<=0))throw new InvalidOperationException("Stone finishing inverted a face");
    var rotation=oldN.sqrMagnitude>1e-14f?Quaternion.FromToRotation(oldN,newN):Quaternion.identity;
    var old=new[]{f.a,f.b,f.c};var moved=new[]{a,b,c};var ns=new[]{f.na,f.nb,f.nc};
    for(int i=0;i<3;i++){
     bool pinned=pin.Contains(Key(old[i]));if(pinned){if(moved[i]!=old[i])throw new InvalidOperationException("Engraving/contact guard moved");result.protectedCorners++;}
     else if((old[i]-moved[i]).sqrMagnitude>1e-12f)result.movedCorners++;
     vertices.Add(filter.transform.InverseTransformPoint(moved[i]));
     outputNormals.Add(filter.transform.InverseTransformDirection(pinned?ns[i]:rotation*ns[i]).normalized);
     var pos=old[i];uv.Add((f.projection==0?new Vector2(pos.x,pos.y):f.projection==1?new Vector2(pos.z,pos.y):new Vector2(pos.x,pos.z))*.65f);
     projectionIds.Add(new Vector2(f.projection,0));
    }
   }
   var mesh=new Mesh{name="RG_Stone_HandDressed_v14"};mesh.vertices=vertices.ToArray();mesh.normals=outputNormals.ToArray();mesh.uv=uv.ToArray();mesh.uv2=projectionIds.ToArray();mesh.triangles=Enumerable.Range(0,vertices.Count).ToArray();mesh.RecalculateBounds();filter.sharedMesh=mesh;
   result.finishedTriangles=faces.Count;return result;
  }
 }
}
