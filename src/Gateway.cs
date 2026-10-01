using System;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using RunicStorageNetwork.Logic;

namespace RunicStorageNetwork {
 public sealed class Gateway:NetworkMember,Hoverable,Interactable,TextReceiver {
  internal const string PrefabName="RSN_RunicGateway",TagKey="rsn_gateway_tag",BindingKey="rsn_gateway_network";
  Renderer[] glow;MaterialPropertyBlock block;string visualState,hover;float nextVisual,nextHover;int hoverRevision=-1;
  protected override void Awake(){base.Awake();glow=GetComponentsInChildren<Renderer>(true).Where(r=>r.sharedMaterial&&new[]{"RG_Core","RG_Runes","RG_BaseRune","RG_EitrChannels"}.Contains(r.sharedMaterial.name)).ToArray();block=new MaterialPropertyBlock();}
  protected override void Update(){
   base.Update();if(!Valid||Time.unscaledTime<nextVisual)return;nextVisual=Time.unscaledTime+.25f;
   string state=Status();if(visualState==state)return;visualState=state;
   foreach(var r in glow){r.GetPropertyBlock(block);block.SetFloat("_EmissionStrength",r.sharedMaterial.GetFloat("_EmissionStrength")*(state=="connected"?1:.12f));r.SetPropertyBlock(block);}
  }
  string Status()=>!ContentSettings.GatewayEnabled?"content_disabled":!Plugin.Enabled?"disabled":!UnloadedNetworks.Enabled?"gateway_experimental":Topology.Graph.GatewayStates.TryGetValue(Id,out var state)?state:"disconnected";
  public string GetHoverName()=>RsnLocalization.Text("gateway_name");
  public float GetHoverOffset()=>1.5f;
  public string GetHoverText(){
   if(!Valid)return GetHoverName();if(hover!=null&&hoverRevision==Topology.DisplayRevision&&Time.unscaledTime<nextHover)return hover;
   if(!ContentSettings.GatewayEnabled)return GetHoverName()+"\n"+RsnLocalization.Text("content_disabled");
   nextHover=Time.unscaledTime+.25f;if(UnloadedNetworks.Requested&&Player.m_localPlayer)UnloadedMultiplayer.Touch(transform.position,Player.m_localPlayer.GetPlayerID());Topology.Refresh();hoverRevision=Topology.DisplayRevision;
   string tag=GetText();hover=GetHoverName()+(tag.Length>0?"\n"+RsnLocalization.Text("gateway_link",tag):"")+"\n"+RsnLocalization.Text(Status())+"\n"+Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] ")+RsnLocalization.Text("gateway_edit");return hover;
  }
  bool CanEdit(Player p)=>ContentSettings.GatewayEnabled&&Valid&&p&&p==Player.m_localPlayer&&!p.IsDead()&&Vector3.Distance(p.transform.position,transform.position)<=5&&Access.Ward(transform.position,p.GetPlayerID());
  public bool Interact(Humanoid user,bool hold,bool alt){if(hold||!(user is Player p)||!CanEdit(p))return false;TextInput.instance.RequestText(this,RsnLocalization.Text("gateway_tag_input"),NetworkLabels.MaxLength);return true;}
  public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
  public string GetText()=>Valid?NetworkLabels.Normalize(View.GetZDO().GetString(TagKey,"")):"";
  public void SetText(string value){if(!CanEdit(Player.m_localPlayer))return;View.ClaimOwnership();if(!View.IsOwner())return;View.GetZDO().Set(TagKey,NetworkLabels.Normalize(value));Topology.Dirty();}
  internal static GameObject Register(AssetBundle bundle){
   var prefab=bundle.LoadAsset<GameObject>("assets/runicstoragegame/rsn_runicgateway.prefab");if(!prefab)throw new InvalidOperationException("Runic Gateway asset missing");
   prefab.SetActive(false);foreach(var t in prefab.GetComponentsInChildren<Transform>(true))t.gameObject.layer=LayerMask.NameToLayer("piece");
   var view=prefab.AddComponent<ZNetView>();view.m_persistent=true;view.m_type=ZDO.ObjectType.Default;
   var piece=prefab.AddComponent<Piece>();piece.m_name="$rsn_gateway_name";piece.m_description="$rsn_gateway_description";piece.m_canBeRemoved=true;piece.m_usage=Piece.UsageTagFlags.Storage|Piece.UsageTagFlags.Crafting;
   piece.m_icon=bundle.LoadAsset<Sprite>("assets/runicstoragegame/rsn_gatewayicon.png");
   var wear=prefab.AddComponent<WearNTear>();wear.m_health=1200;wear.m_materialType=WearNTear.MaterialType.Stone;wear.m_noRoofWear=true;wear.m_noSupportWear=false;wear.m_burnable=false;
   prefab.AddComponent<Gateway>();
   var config=new PieceConfig{Name=piece.m_name,Description=piece.m_description,PieceTable="Hammer",CraftingStation="piece_workbench",Category="Crafting",Usage=new[]{"Storage","Crafting"},Requirements=new[]{new RequirementConfig("Stone",20,0,true),new RequirementConfig("YggdrasilWood",10,0,true),new RequirementConfig("Silver",6,0,true),new RequirementConfig("Crystal",10,0,true),new RequirementConfig("Eitr",5,0,true)}};
   if(!PieceManager.Instance.AddPiece(new CustomPiece(prefab,false,config)))throw new InvalidOperationException("Jotunn rejected gateway");prefab.SetActive(true);return prefab;
  }
 }
}
