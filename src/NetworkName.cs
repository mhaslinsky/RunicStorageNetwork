using UnityEngine;
using RunicStorageNetwork.Logic;

namespace RunicStorageNetwork {
 public sealed class NetworkName:MonoBehaviour,Interactable,TextReceiver {
  internal const string Key="rsn_display_name";
  Core editing;
  Core Target {
   get {var member=GetComponent<NetworkMember>();return member&&member.Valid?Topology.LabelRootSnapshot(member.Network):null;}
  }
  internal static string Read(Core core)=>core&&core.Valid?NetworkLabels.Normalize(R.View(core).GetZDO().GetString(Key,"")):"";
  internal static string For(NetworkMember member)=>member&&member.Valid?Read(Topology.LabelRootSnapshot(member.Network)):"";
  bool CanEdit(Player player,Core target)=>player&&target&&target.Valid&&GetComponent<Core>().Valid&&
   Vector3.Distance(player.transform.position,transform.position)<=10f&&Access.Ward(transform.position,player.GetPlayerID())&&Access.Ward(target.transform.position,player.GetPlayerID());
  public bool Interact(Humanoid user,bool hold,bool alt){
   if(hold||!(user is Player player)||player!=Player.m_localPlayer)return false;
   Topology.Refresh();var target=Target;if(!CanEdit(player,target))return false;
   editing=target;TextInput.instance.RequestText(this,RsnLocalization.Text("network_name_input"),NetworkLabels.MaxLength);return true;
  }
  public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>BuilderCodex.Use(GetComponent<Core>(),user,item);
  public string GetText()=>Read(editing?editing:Target);
  public void SetText(string value){
   Topology.Refresh();var target=Target;
   if(target!=editing||!CanEdit(Player.m_localPlayer,target)){editing=null;return;}
   // Match vanilla sign editing: claim the view, then persist text in its synchronized ZDO.
   var view=R.View(target);view.ClaimOwnership();
   if(view.IsOwner())view.GetZDO().Set(Key,NetworkLabels.Normalize(value));
   editing=null;
  }
 }
}
