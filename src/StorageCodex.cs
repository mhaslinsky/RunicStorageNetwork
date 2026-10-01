using UnityEngine;

namespace RunicStorageNetwork {
 // An access point inside supply coverage, not a relay or another network root.
 public sealed class StorageCodex:MonoBehaviour,Hoverable,Interactable {
  ZNetView view;float nextHover;string hover;Player hoverPlayer;int hoverRevision=-1;
  void Awake(){view=GetComponent<ZNetView>();}
  internal bool Valid=>R.Valid(view)&&GetComponent<Piece>()&&GetComponent<Piece>().IsPlacedByPlayer();
  internal ZDOID Id=>view.GetZDO().m_uid;
  internal static StorageCodex Find(ZDOID id){var go=ZNetScene.instance?ZNetScene.instance.FindInstance(id):null;return go?go.GetComponent<StorageCodex>():null;}
  public bool Interact(Humanoid user,bool hold,bool alt){
   if(hold||!ContentSettings.TerminalEnabled||!Valid||!(user is Player player)||player!=Player.m_localPlayer)return false;
   var core=Core.Choose(transform.position,player.GetPlayerID());return NetworkTerminal.Open(this,core,player);
  }
  public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
  public string GetHoverName()=>Localization.instance.Localize("$rsn_codex_name");
  public float GetHoverOffset()=>.8f;
  public string GetHoverText(){
   if(!ContentSettings.TerminalEnabled)return GetHoverName()+"\n"+RsnLocalization.Text("content_disabled");
   var player=Player.m_localPlayer;if(!Valid||!player)return GetHoverName();
   if(hover!=null&&hoverPlayer==player&&hoverRevision==Topology.DisplayRevision&&Time.unscaledTime<nextHover)return hover;
   hoverPlayer=player;hoverRevision=Topology.DisplayRevision;nextHover=Time.unscaledTime+.25f;
   string state;Core core=null;
   if(!Plugin.Enabled)state=RsnLocalization.Text("disabled");
   else if(!Access.Ward(transform.position,player.GetPlayerID()))state=RsnLocalization.Text("error_access");
   else{
    if(UnloadedNetworks.Enabled)Core.Choose(transform.position,player.GetPlayerID());
    // Normal mode consumes NetworkSystem's snapshot; the experiment primes discovery on demand.
    var graph=Topology.ForActor(player.GetPlayerID());string network=graph.Choose(Topology.Position(transform.position),n=>true);
    if(network!=null&&graph.Roots.TryGetValue(network,out var id))core=Topology.Member(id)?.GetComponent<Core>();
    var name=core?NetworkName.For(core.GetComponent<NetworkMember>()):"";
    state=core?RsnLocalization.Text(name.Length>0?"chest_connected_named":"chest_connected",name):RsnLocalization.Text("codex_no_network");
   }
   hover=GetHoverName()+"\n"+state;
   if(core)hover+="\n"+Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] $rsn_terminal_open");
   return hover;
  }
 }
}
