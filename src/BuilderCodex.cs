using System;
using UnityEngine;
using RunicStorageNetwork.Logic;

namespace RunicStorageNetwork {
 internal static class BuilderCodex {
  internal const string BindingKey="rsn_builder_network",NameKey="rsn_builder_network_name";
  static readonly int itemHash=BuilderCodexItem.PrefabName.GetStableHashCode();
  internal static bool Is(ItemDrop.ItemData item)=>item?.m_dropPrefab&&item.m_dropPrefab.name==BuilderCodexItem.PrefabName;
  static string Read(ItemDrop.ItemData item,string key)=>item.m_customData!=null&&item.m_customData.TryGetValue(key,out var value)?value:"";
  // null: ordinary supply; empty: equipped but unbound, so no network building.
  internal static string Binding(Player player){
   if(!player)return null;
   if(player!=Player.m_localPlayer)return RemoteBinding(R.View(player)?.GetZDO());
   var item=R.Get<ItemDrop.ItemData>(player,"m_utilityItem");
   string binding=ContentSettings.BuilderEnabled&&Is(item)&&item.m_equipped&&player.GetInventory().ContainsItem(item)?Read(item,BindingKey):null;
   var view=R.View(player);
   if(R.Valid(view)&&view.IsOwner()&&view.GetZDO().GetString(BindingKey,"")!=(binding??"")){
    view.GetZDO().Set(BindingKey,binding??"");ZDOMan.instance.ForceSendZDO(view.GetZDO().m_uid);
   }
   return binding;
  }
  // Equipment already has a native synchronized prefab hash. Pair the item's
  // binding with that hash, not with a client-supplied range/permission claim.
  internal static string RemoteBinding(ZDO actor)=>ContentSettings.BuilderEnabled&&actor!=null&&actor.GetInt(ZDOVars.s_utilityItem,0)==itemHash?actor.GetString(BindingKey,""):null;
  internal static string ForOperation(Operation op)=>!op.Build?null:Player.m_localPlayer&&Player.m_localPlayer.GetZDOID()==op.Actor?Binding(Player.m_localPlayer):RemoteBinding(RemoteContext.Data(op.Actor));
  internal static bool Building(long actor){var p=Player.m_localPlayer;return p&&p.GetPlayerID()==actor&&p.InPlaceMode()&&Binding(p)!=null;}
  internal static void EquipmentChanged(Humanoid user){if(user is Player p&&p==Player.m_localPlayer)Binding(p);}
  internal static bool Use(Core core,Humanoid user,ItemDrop.ItemData item){
   if(!Is(item))return false;
   if(!ContentSettings.BuilderEnabled)return true;
   if(!(user is Player p)||p!=Player.m_localPlayer||!core||!core.Valid||!p.GetInventory().ContainsItem(item))return true;
   if(Vector3.Distance(p.transform.position,core.transform.position)>10f||!Access.Ward(core.transform.position,p.GetPlayerID()))return true;
   if(Actions.Locked(p.GetInventory())||Transport.Locked(p.GetInventory())||Transport.LockedItem(item))return true;
   var member=core.GetComponent<NetworkMember>();if(!member||string.IsNullOrEmpty(member.SavedNetwork))return true;
   Topology.Refresh();string name=NetworkName.For(member);
   if(item.m_customData==null)item.m_customData=new System.Collections.Generic.Dictionary<string,string>();
   item.m_customData[BindingKey]=member.SavedNetwork;item.m_customData[NameKey]=name;
   R.Call(p.GetInventory(),"Changed",new[]{typeof(bool),typeof(bool)},false,false);Binding(p);
   p.Message(MessageHud.MessageType.TopLeft,RsnLocalization.Text("builder_bound",name==""?RsnLocalization.Text("builder_unnamed"):name));
   return true;
  }
  internal static string Tooltip(ItemDrop.ItemData item){
   if(!Is(item))return "";
   if(!ContentSettings.BuilderEnabled)return "\n\n"+RsnLocalization.Text("content_disabled");
   string binding=Read(item,BindingKey),name=NetworkLabels.Normalize(Read(item,NameKey));
   // Read an already available name snapshot; hovering an inventory item never
   // wakes distant storage or rebuilds topology.
   string network=Topology.Graph.BoundNetwork(binding);var root=Topology.LabelRootSnapshot(network);
   if(root)name=NetworkName.Read(root);
   string state=binding==""?RsnLocalization.Text("builder_unbound"):RsnLocalization.Text("builder_binding",name==""?RsnLocalization.Text("builder_unnamed"):name);
   return "\n\n"+state+"\n"+RsnLocalization.Text("builder_range",Plugin.RelayLink.Value)+"\n"+RsnLocalization.Text("builder_bind_hint");
  }
 }
}
