using System;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace RunicStorageNetwork {
 internal static class BuilderCodexItem {
  internal const string PrefabName="RSN_RunicBuilderCodex";
  internal static GameObject Register(AssetBundle bundle){
   var prefab=bundle.LoadAsset<GameObject>("assets/runicstoragegame/rsn_runicbuildercodex.prefab");
   var icon=bundle.LoadAsset<Sprite>("assets/runicstoragegame/rsn_buildercodexicon.png");
   if(!prefab||prefab.name!=PrefabName||!icon||!prefab.transform.Find("attach")||!prefab.transform.Find("attach_Hips")||!prefab.GetComponent<BoxCollider>())throw new InvalidOperationException("Runic Builder's Codex assets missing");
   prefab.SetActive(false);
   foreach(var t in prefab.GetComponentsInChildren<Transform>(true))t.gameObject.layer=LayerMask.NameToLayer("item");
   // Vanilla utility equipment clones attach_Hips onto the named player bone.
   // Keep it hidden on the dropped item; the separate attach visual is used there.
   prefab.transform.Find("attach_Hips").gameObject.SetActive(false);
   var body=prefab.AddComponent<Rigidbody>();body.mass=1;body.useGravity=true;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
   var view=prefab.AddComponent<ZNetView>();view.m_persistent=true;view.m_type=ZDO.ObjectType.Default;
   var sync=prefab.AddComponent<ZSyncTransform>();sync.m_syncPosition=true;sync.m_syncRotation=true;sync.m_syncBodyVelocity=true;
   var drop=prefab.AddComponent<ItemDrop>();
   drop.m_itemData.m_shared=new ItemDrop.ItemData.SharedData{
    m_name="$rsn_builder_codex_name",m_description="$rsn_builder_codex_description",m_itemType=ItemDrop.ItemData.ItemType.Utility,
    m_maxStackSize=1,m_maxQuality=1,m_weight=1,m_teleportable=true,m_useDurability=false,m_icons=new[]{icon}
   };
   drop.m_itemData.m_dropPrefab=prefab;drop.m_itemData.m_stack=1;drop.m_itemData.m_quality=1;
   var config=new ItemConfig{
    Name=drop.m_itemData.m_shared.m_name,Description=drop.m_itemData.m_shared.m_description,Icon=icon,
    CraftingStation="blackforge",MinStationLevel=1,Amount=1,Enabled=ContentSettings.BuilderEnabled,RequireOnlyOneIngredient=false,
    Requirements=new[]{new RequirementConfig(RunicCodexItem.PrefabName,1),new RequirementConfig("BlackCore",1),new RequirementConfig("Eitr",5),new RequirementConfig("Silver",2),new RequirementConfig("Crystal",2)}
   };
   if(!ItemManager.Instance.AddItem(new CustomItem(prefab,false,config)))throw new InvalidOperationException("Jotunn rejected Runic Builder's Codex");
   prefab.SetActive(true);Plugin.Info(PrefabName+" registered with black forge recipe");return prefab;
  }
 }
}
