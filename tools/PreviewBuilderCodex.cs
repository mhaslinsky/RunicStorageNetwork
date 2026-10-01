using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RunicStorage.Build {
 public static partial class BuildAssets {
  // Editor-only approximation on the installed vanilla rig. Native meshes are
  // read and baked in memory for a neutral mannequin; never saved or bundled.
  static void PreviewBuilderEquipment(GameObject prefab,string output){
   if(!iconDonors)iconDonors=AssetBundle.LoadFromFile(@"E:\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles\c4210710");
   var native=iconDonors.LoadAsset<GameObject>("Assets/Characters/Player/Player.prefab");Check(native,"Vanilla player preview unavailable");
   var player=UnityEngine.Object.Instantiate(native);player.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
   var stage=new GameObject("RSN_RunicBuilderCodex_EquippedPreview");
   var bodyMesh=new Mesh();var bodyMat=new Material(Shader.Find("Standard")){name="PreviewMannequin",color=new Color(.25f,.29f,.31f)};bodyMat.SetFloat("_Glossiness",.05f);
   try{
    var skin=player.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.name=="body");
    var hips=skin.bones.Single(t=>t.name=="Hips");
    // Same rigid attachment sequence as VisEquipment.AttachArmor in the installed game.
    var worn=UnityEngine.Object.Instantiate(prefab.transform.Find("attach_Hips").gameObject,hips,true);
    worn.SetActive(true);worn.transform.localPosition=Vector3.zero;worn.transform.localRotation=Quaternion.identity;
    Check(Vector3.Distance(worn.transform.lossyScale,Vector3.one)<.0001f,"Bone inherited scale distorts accessory");
    Check(worn.GetComponentsInChildren<MeshRenderer>().Length==9&&worn.GetComponentsInChildren<Collider>().Length==0,"Equipment visual invalid");
    var bounds=BuilderBounds(worn);var relative=bounds.center-hips.position;
    Check(bounds.size.y>.48f&&bounds.size.y<.51f&&bounds.min.y>.50f&&bounds.max.y<1.30f,"Book belt height/scale invalid: "+bounds);
    Check(relative.x>.17f&&relative.x<.40f,"Book must hang outside the right hip: "+relative);
    // Verify a rotated/moved rig carries the book, rather than leaving it at the spawn point.
    var book=worn.transform.Find("Book");var center=hips.InverseTransformPoint(book.position);var rotation=hips.localRotation;
    hips.localRotation*=Quaternion.Euler(0,35,0);player.transform.position=new Vector3(18,4,-12);
    Check(Vector3.Distance(book.position,hips.TransformPoint(center))<.001f,"Book does not follow rig motion");
    hips.localRotation=rotation;player.transform.position=Vector3.zero;
    skin.BakeMesh(bodyMesh,false);
    var body=new GameObject("Vanilla rig mannequin");body.transform.SetParent(stage.transform,false);
    body.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);
    // The imported rig's bind poses already bake its centimetre conversion.
    // Applying the renderer's 95x transform again would scale the mannequin twice.
    body.AddComponent<MeshFilter>().sharedMesh=bodyMesh;
    var bodyRenderer=body.AddComponent<MeshRenderer>();bodyRenderer.sharedMaterials=Enumerable.Repeat(bodyMat,bodyMesh.subMeshCount).ToArray();
    worn.transform.SetParent(stage.transform,true);
    Check(bodyRenderer.bounds.size.y>1.6f&&bodyRenderer.bounds.size.y<2.2f,"Mannequin scale invalid: "+bodyRenderer.bounds);
    // Turn the studio subject toward the existing key light; attachment offsets
    // above stay in the actual player coordinate system.
    stage.transform.rotation=Quaternion.Euler(0,180,0);
    TerminalView(stage,Path.Combine(output,"builder-codex-equipped.png"),new Vector3(-4,1.5f,-6),false,framing:1.08f,ground:true);
    TerminalView(stage,Path.Combine(output,"builder-codex-equipped-close.png"),new Vector3(-5,1,-4),true,framing:.47f);
    File.WriteAllText(Path.Combine(output,"BuilderCodexFitReport.json"),"{\"rig\":\"installed vanilla Player\",\"bone\":\"Hips\",\"nativeAttachSequence\":true,\"boneMotionCheck\":true,\"oneVisibleBook\":true,\"worldScale\":1,\"preview\":\"Editor neutral mannequin, not gameplay\",\"animationClippingVerified\":false}");
   }finally{
    UnityEngine.Object.DestroyImmediate(stage);UnityEngine.Object.DestroyImmediate(player);UnityEngine.Object.DestroyImmediate(bodyMesh);UnityEngine.Object.DestroyImmediate(bodyMat);
   }
  }
 }
}
