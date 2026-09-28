using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RunicStorageNetwork.Logic;

namespace RunicStorageNetwork {
 public sealed class Core:MonoBehaviour,Hoverable {
  internal static readonly HashSet<Core> Live=new HashSet<Core>();
  internal readonly List<Container> Pool=new List<Container>();
  readonly HashSet<Inventory> subscribed=new HashSet<Inventory>();
  sealed class Cached {internal float Until;internal List<Stock> Items;}
  readonly Dictionary<long,Cached> cached=new Dictionary<long,Cached>();
  ZNetView view;bool registered;
  internal bool Valid=>R.Valid(view)&&GetComponent<Piece>().IsPlacedByPlayer();
  internal ZDOID Id=>view.GetZDO().m_uid;
  void Awake(){view=GetComponent<ZNetView>();}
  void Update(){
   if(!Valid){if(registered){Live.Remove(this);registered=false;Pool.Clear();}return;}
   if(!registered){Live.Add(this);registered=true;Plugin.Debug("Core active "+R.Key(Id));}
  }
  internal void Scan(){
   int previous=Pool.Count;Pool.Clear();if(!Valid)return;
   Pool.AddRange(Topology.Pool(this).Where(c=>c&&R.Valid(R.View(c))));
   Pool.Sort((a,b)=>string.CompareOrdinal(R.Key(R.View(a).GetZDO().m_uid),R.Key(R.View(b).GetZDO().m_uid)));
   var inventories=new HashSet<Inventory>(Pool.Select(c=>c.GetInventory()).Where(i=>i!=null));
   foreach(var inv in subscribed.Where(i=>!inventories.Contains(i)).ToArray()){inv.m_onChanged-=Invalidate;subscribed.Remove(inv);}
   foreach(var inv in inventories)if(subscribed.Add(inv))inv.m_onChanged+=Invalidate;
   Invalidate();
   if(previous!=Pool.Count)Plugin.Debug("Core "+R.Key(Id)+" candidate containers="+Pool.Count);
  }
  internal void Invalidate(){cached.Clear();}
  internal List<Stock> Stock(long player){
   if(cached.TryGetValue(player,out var hit)&&Time.unscaledTime<hit.Until)return hit.Items;
   var result=new List<Stock>();foreach(var c in Pool)if(Access.Container(c,player,this,out _,preview:true))result.AddRange(Stockroom.Preview(c));
   cached[player]=new Cached{Until=Time.unscaledTime+0.25f,Items=result};return result;
  }
  internal void Detach(){Live.Remove(this);Pool.Clear();foreach(var inv in subscribed)inv.m_onChanged-=Invalidate;subscribed.Clear();cached.Clear();}
  void OnDestroy(){Detach();if(registered)Plugin.Debug("Core removed");}
  internal static Core Choose(Vector3 point,long player){
   if(!Plugin.Enabled)return null;
   return Topology.Choose(point,player);
  }
  public string GetHoverName()=>Localization.instance.Localize("$rsn_name");
  public float GetHoverOffset()=>1.5f;
  public string GetHoverText()=>HoverInfo.Text(GetComponent<NetworkMember>());
  internal static void Diagnose(Terminal terminal){
   var p=Player.m_localPlayer;if(!p){terminal.AddString("[RSN] "+RsnLocalization.Text("diag_no_player"));return;}
   var core=Choose(p.transform.position,p.GetPlayerID());string text="[RSN] supply="+Plugin.Enabled+" core="+(core?R.Key(core.Id):"none");terminal.AddString("[RSN] "+RsnLocalization.Text("diag_supply",RsnLocalization.Text(Plugin.Enabled?"enabled":"disabled"),core?R.Key(core.Id):RsnLocalization.Text("none")));Plugin.Info(text);
   if(!core)return;core.Scan();foreach(var c in core.Pool){bool ok=Access.Container(c,p.GetPlayerID(),core,out string why);string id=R.Key(R.View(c).GetZDO().m_uid);text=id+" "+(ok?"available":why);terminal.AddString(RsnLocalization.Text("diag_chest",id,RsnLocalization.Reason(ok?"available":why)));Plugin.Info(text);}
  }
 }
 internal static class Access {
  internal static bool Ward(Vector3 point,long player){
   if(UnloadedNetworks.Enabled)return UnloadedNetworks.Ward(point,player);
   bool denied=false,allowed=false;
   foreach(var area in R.Get<List<PrivateArea>>(typeof(PrivateArea),"m_allAreas")) {
    if(!area||!(bool)R.Call(area,"IsEnabled",Type.EmptyTypes)||!(bool)R.Call(area,"IsInside",new[]{typeof(Vector3),typeof(float)},point,0f))continue;
    if(area.GetComponent<Piece>().GetCreator()==player||(bool)R.Call(area,"IsPermitted",new[]{typeof(long)},player))allowed=true;else denied=true;
   }
   return allowed||!denied;
  }
  internal static bool Container(Container c,long player,Core core,out string reason,bool ownLease=false,string reservation=null,DisplayAccessCache display=null,bool preview=false){
   reason="unloaded";var v=R.View(c);if(!c||!R.Valid(v)||!core||!core.Valid)return false;
   reason=ContainerPolicy.Reason(R.Id(c.gameObject));if(reason!=null)return false;
   var piece=c.GetComponent<Piece>();reason="not player built";if(!piece||!piece.IsPlacedByPlayer())return false;
   reason="moving/private";if(c.m_privacy!=global::Container.PrivacySetting.Public||c.m_wagon||c.m_rootObjectOverride||c.GetComponentInParent<Ship>()||c.GetComponentInParent<Rigidbody>())return false;
   reason="network path/storage coverage unavailable";if(!(display==null?Topology.Covers(core,c.transform.position,player):display.Covers(core,c.transform.position,player)))return false;
   reason="unconfirmed loaded area";if(!UnloadedNetworks.CanUseUnloaded(c)&&!(display==null?ZNetScene.instance.IsAreaReady(c.transform.position):display.Ready(c.transform.position)))return false;
   reason="access denied";if(!(bool)R.Call(c,"CheckAccess",new[]{typeof(long)},player)||!(display==null?Ward(c.transform.position,player):display.Ward(c.transform.position,player))||!(display==null?Ward(core.transform.position,player):display.Ward(core.transform.position,player)))return false;
   reason="inventory unavailable";if(c.GetInventory()==null)return false;
   bool reservedPreview=preview&&(v.GetZDO().GetString("rsn_lease","")!=""||Transport.Reserved(v.GetZDO()));
   reason="busy/reserved";if(!ownLease&&!reservedPreview&&(c.IsInUse()||v.GetZDO().GetInt(ZDOVars.s_inUse)!=0||Transport.Reserved(v.GetZDO(),reservation)||Integrations.IsBusy(c.GetInventory())))return false;
   reason="available";return true;
  }
 }
 internal static class Stockroom {
  sealed class Observation {internal byte[] Data;internal List<Stock> Items;}
  static readonly Dictionary<string,Observation> observed=new Dictionary<string,Observation>();
  internal static void ClearObservations(){observed.Clear();}
  internal static void Observe(string key,List<Stock> items){
   var z=RemoteContext.Source(key);if(z==null)return;observed[key]=new Observation{Data=(byte[])(z.GetByteArray(ZDOVars.s_items)??Array.Empty<byte>()).Clone(),Items=items};
   foreach(var core in Core.Live)if(core)core.Invalidate();CraftOverview.Fresh(key,items);if(z.GetOwner()!=ZNet.GetUID())ZDOMan.instance.RequestZDO(z.m_uid);
  }
  internal static List<Stock> Preview(Container c){
   var z=R.View(c).GetZDO();string key=R.Key(z.m_uid);var items=Snapshot(c.GetInventory(),key,null,true);
   if(R.View(c).IsOwner())return items;
   if(observed.TryGetValue(key,out var fresh)){
    // Elapsed time cannot make an older local inventory more authoritative.
    // Replace these display counts on a new owner snapshot or synchronized data;
    // payment always obtains fresh owner confirmation independently.
    if(!fresh.Data.SequenceEqual(z.GetByteArray(ZDOVars.s_items)??Array.Empty<byte>()))observed.Remove(key);
    else {var names=new HashSet<string>(fresh.Items.Select(s=>s.Item));items.RemoveAll(s=>names.Contains(s.Item));items.AddRange(fresh.Items);}
   }
   return items;
  }
  internal static void Refresh(Core core,IEnumerable<string> sources){
   var selected=new HashSet<string>(sources);int requests=0;
   core.Scan();foreach(var c in core.Pool){var v=R.View(c);if(!R.Valid(v)||!selected.Contains(R.Key(v.GetZDO().m_uid))||Transport.Reserved(v.GetZDO())||Transport.Locked(c.GetInventory()))continue;R.Call(c,"Load");if(!v.IsOwner()&&requests++<32)ZDOMan.instance.RequestZDO(v.GetZDO().m_uid);}
   core.Invalidate();
  }
  internal static bool Ingredient(ItemDrop.ItemData item,bool chest){
   if(item==null||!item.m_dropPrefab||item.m_stack<=0||item.m_worldLevel<Game.m_worldLevel)return false;
   // Recipes select ingredients by prefab and quality, not item category or metadata.
   // Use the same eligibility for browsing, owner snapshots and actual payment.
   return !chest||!item.m_equipped;
  }
  internal static List<Stock> Snapshot(Inventory inventory,string source,IEnumerable<Need> needs,bool chest){
   var ids=needs==null?null:new HashSet<string>(needs.Select(n=>n.Item));
   return inventory.GetAllItems().Where(i=>Ingredient(i,chest)&&(ids==null||ids.Contains(i.m_dropPrefab.name))).GroupBy(i=>new {Id=i.m_dropPrefab.name,i.m_quality}).Select(g=>new Stock(source,g.Key.Id,g.Key.m_quality,g.Sum(i=>i.m_stack))).ToList();
  }
  internal static List<Need> Requirements(Piece.Requirement[] requirements,int quality,int multiplier){
   return requirements.Where(r=>r.m_resItem&&!r.m_upgraderResource&&r.GetAmount(quality)>0).Select(r=>new Need(r.m_resItem.name,checked(r.GetAmount(quality)*multiplier))).ToList();
  }
  internal static List<Stock> Available(Player player,Core core,List<Need> needs){
   var all=Snapshot(player.GetInventory(),"player",needs,false);
   if(core)all.AddRange(StorageIndex.Query(core,player.GetPlayerID(),needs));
   return all;
  }
  internal static bool Qualities(List<Need> needs,List<Stock> stock,bool craft){
   if(!craft)return true;
   foreach(var n in needs){var group=stock.Where(s=>s.Item==n.Item&&s.Allowed).GroupBy(s=>s.Quality).OrderBy(g=>g.Key).FirstOrDefault(g=>g.Sum(s=>s.Amount)>=n.Amount);if(group==null)return false;n.Quality=group.Key;}return true;
  }
 }
 internal sealed class InventoryDelta {
  internal sealed class Part {internal ItemDrop.ItemData Item;internal int Amount,Removed;internal Vector2i Position;}
  internal readonly Inventory Inventory;internal readonly List<Part> Parts=new List<Part>();internal bool Applied;
  internal InventoryDelta(Inventory inventory,IEnumerable<Debit> debits,bool chest){
   Inventory=inventory;var used=new Dictionary<ItemDrop.ItemData,int>();
   foreach(var d in debits){int left=d.Amount;foreach(var item in inventory.GetAllItems().Where(i=>Stockroom.Ingredient(i,chest)&&i.m_dropPrefab.name==d.Item&&i.m_quality==d.Quality)){
    used.TryGetValue(item,out int prior);int n=Math.Min(left,item.m_stack-prior);if(n<=0)continue;var part=Parts.FirstOrDefault(x=>x.Item==item);if(part==null){part=new Part{Item=item,Position=item.m_gridPos};Parts.Add(part);}part.Amount+=n;used[item]=prior+n;left-=n;if(left==0)break;}
    if(left!=0)throw new InvalidOperationException("Fresh stock insufficient: "+d.Item);
   }
  }
  internal void Apply(){if(Applied)return;foreach(var part in Parts)if(!Inventory.ContainsItem(part.Item)||part.Item.m_stack<part.Amount)throw new InvalidOperationException("Stale inventory");
   Applied=true;try {foreach(var part in Parts){int before=part.Item.m_stack;try {if(!Inventory.RemoveItem(part.Item,part.Amount))throw new InvalidOperationException("RemoveItem refused");}finally {part.Removed=before-(Inventory.ContainsItem(part.Item)?part.Item.m_stack:0);}if(part.Removed!=part.Amount)throw new InvalidOperationException("Unexpected debit amount");}}
   catch {Restore();throw;}
  }
  internal void Restore(){if(!Applied)return;foreach(var p in Parts.Where(x=>x.Removed>0)){
   int before=Inventory.ContainsItem(p.Item)?p.Item.m_stack:0;int amount=p.Removed;
   try {if(Inventory.ContainsItem(p.Item))p.Item.m_stack+=amount;
    else {if(Inventory.GetItemAt(p.Position.x,p.Position.y)!=null)throw new InvalidOperationException("Reserved rollback slot occupied");var parcel=p.Item.Clone();parcel.m_stack=amount;
     try {if(!(bool)R.Call(Inventory,"AddItem",new[]{typeof(ItemDrop.ItemData),typeof(int),typeof(int),typeof(int),typeof(bool)},parcel,amount,p.Position.x,p.Position.y,false))throw new InvalidOperationException("Delta restoration refused");}
     finally {var placed=Inventory.GetItemAt(p.Position.x,p.Position.y);if(placed!=null&&placed.m_dropPrefab==p.Item.m_dropPrefab&&placed.m_quality==p.Item.m_quality)p.Item=placed;}}}
   finally {int restored=(Inventory.ContainsItem(p.Item)?p.Item.m_stack:0)-before;p.Removed-=Math.Max(0,Math.Min(amount,restored));}
  }Applied=Parts.Any(p=>p.Removed>0);R.Call(Inventory,"Changed",new[]{typeof(bool),typeof(bool)},false,false);}
 }
}
