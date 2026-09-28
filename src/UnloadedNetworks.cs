using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace RunicStorageNetwork {
 // Experimental, single-player only. Inactive adapters expose saved records to the
 // existing transaction code. They never enter ZNetScene, run Awake/Update, or carry
 // renderers, colliders, WearNTear, default loot or third-party prefab behaviours.
 internal sealed class UnloadedReplica:MonoBehaviour {
  internal ZDO Data;internal byte[] Expected;internal bool Read,SavedAfterLoad;
  internal uint FailedRevision;internal bool ReportedFailure;
 }
 internal static class UnloadedNetworks {
  static readonly HashSet<int> chestTypes=new HashSet<int>();
  static readonly HashSet<int> nodeTypes=new HashSet<int>(new[]{"RSN_NetworkCore","RSN_RunicRelay"}.Select(s=>s.GetStableHashCode()));
  static readonly Dictionary<ZDOID,ZDO> nodes=new Dictionary<ZDOID,ZDO>();
  static readonly Dictionary<ZDOID,ZDO> chests=new Dictionary<ZDOID,ZDO>();
  static readonly Dictionary<ZDOID,ZDO> wards=new Dictionary<ZDOID,ZDO>();
  static readonly Dictionary<Vector2s,HashSet<ZDOID>> chestSectors=new Dictionary<Vector2s,HashSet<ZDOID>>();
  static readonly Dictionary<ZDOID,Vector2s> positions=new Dictionary<ZDOID,Vector2s>();
  static readonly Dictionary<ZDOID,(Vector3,string,long,int)> nodeState=new Dictionary<ZDOID,(Vector3,string,long,int)>();
  static readonly Dictionary<ZDOID,long> creators=new Dictionary<ZDOID,long>();
  static readonly Dictionary<(Vector3,long),bool> wardAccess=new Dictionary<(Vector3,long),bool>();
  static readonly Dictionary<ZDOID,GameObject> replicas=new Dictionary<ZDOID,GameObject>();
  static readonly Queue<ZDOID> queue=new Queue<ZDOID>();
  static readonly HashSet<ZDOID> queued=new HashSet<ZDOID>(),requestedChests=new HashSet<ZDOID>();
  static readonly Dictionary<string,long> requestedNetworks=new Dictionary<string,long>();
  static readonly HashSet<int> wardTypes=new HashSet<int>();
  static ZDOMan world;static bool enabled,bootstrapped;static long structure,nodesRequested=-1;static float demandUntil;
  internal static bool Enabled=>enabled&&world==ZDOMan.instance&&ZNet.instance&&ZNet.IsSinglePlayer;
  internal static bool Demand=>Enabled&&Time.unscaledTime<demandUntil;
  internal static IEnumerable<NetworkMember> Members=>replicas.Values.Where(g=>g).Select(g=>g.GetComponent<NetworkMember>()).Where(m=>m&&m.Valid);
  internal static IEnumerable<Container> Containers=>replicas.Values.Where(g=>g).Select(g=>g.GetComponent<Container>()).Where(c=>c&&R.Valid(R.View(c))&&(!ZNetScene.instance.FindInstance(R.View(c).GetZDO().m_uid)||Transport.Leases.ContainsKey(c.GetInventory())));
  static void Hook(Harmony h,Type type,string method,string handler,bool prefix=false,Type[] args=null){
   var target=args==null?AccessTools.Method(type,method):AccessTools.Method(type,method,args);
   if(target==null)throw new MissingMethodException(type.Name,method);
   var patch=new HarmonyMethod(typeof(UnloadedNetworks),handler){priority=prefix?Priority.First:Priority.Last};
   h.Patch(target,prefix:prefix?patch:null,postfix:prefix?null:patch);
  }
  internal static void Install(Harmony h){
   Hook(h,typeof(ZNetScene),"Awake",nameof(Bootstrap));
   Hook(h,typeof(ZNet),"Start",nameof(WorldLoaded));
   Hook(h,typeof(ZNetScene),"OnDestroy",nameof(Shutdown),true);
   Hook(h,typeof(ZDOMan),"AddToSector",nameof(Added));
   Hook(h,typeof(ZDOMan),"HandleDestroyedZDO",nameof(Destroyed),true);
   Hook(h,typeof(ZDO),"IncreaseDataRevision",nameof(Changed));
   Hook(h,typeof(ZNetView),"Awake",nameof(Loaded));
   Hook(h,typeof(ZNetView),"ResetZDO",nameof(Unloading),true);
   Hook(h,typeof(ZNetScene),"RemoveObjects",nameof(PinTransactions),true);
   Hook(h,typeof(Container),"Load",nameof(LoadReplica),true,Type.EmptyTypes);
   Hook(h,typeof(Container),"Save",nameof(SaveReplica),true,Type.EmptyTypes);
   StorageIndex.DemandDriven=()=>Enabled;
   StorageIndex.HasDemand=()=>Demand;
   StorageIndex.AreaReady=SourceReady;
   StorageIndex.ReadFailure=ReadFailure;
   CraftInspection.FindContainer=FindContainer;
  }
  static void Shutdown(){
   foreach(var go in replicas.Values)if(go){var core=go.GetComponent<Core>();if(core)core.Detach();var member=go.GetComponent<NetworkMember>();if(member)Topology.Members.Remove(member);R.Set(go.GetComponent<ZNetView>(),"m_zdo",null);UnityEngine.Object.Destroy(go);}
   replicas.Clear();nodes.Clear();chests.Clear();wards.Clear();chestSectors.Clear();positions.Clear();nodeState.Clear();creators.Clear();wardAccess.Clear();queue.Clear();queued.Clear();requestedChests.Clear();requestedNetworks.Clear();wardTypes.Clear();chestTypes.Clear();world=null;enabled=false;bootstrapped=false;structure=0;nodesRequested=-1;demandUntil=0;
  }
  static void Bootstrap(){
   Shutdown();world=ZDOMan.instance;enabled=Plugin.ExperimentalUnloadedNetworks.Value&&ZNet.IsSinglePlayer;
   if(!enabled){if(Plugin.ExperimentalUnloadedNetworks.Value)Plugin.Info("ExperimentalUnloadedNetworks is restricted to single player; normal networking remains active.");return;}
   // ZNetScene.Awake runs BEFORE ZNet.Start reads both old and chunked saves.
   // Only latch the setting here. Index the completed records after that load.
   Plugin.Info("EXPERIMENTAL unloaded networks enabled (single player); waiting for saved world records. May cause errors. Back up the world before testing.");
  }
  static void WorldLoaded(){
   if(!Enabled||bootstrapped||ZNet.m_loadError)return;
   foreach(var prefab in ZNetScene.instance.m_prefabs)if(prefab){if(prefab.GetComponent<PrivateArea>())wardTypes.Add(prefab.name.GetStableHashCode());if(prefab.GetComponent<Container>())chestTypes.Add(prefab.name.GetStableHashCode());}
   // Exactly one metadata pass AFTER the world load; no inventories are
   // deserialized here. Includes records which have never had a local instance.
   foreach(var z in R.Get<Dictionary<ZDOID,ZDO>>(world,"m_objectsByID").Values)Record(z);
   bootstrapped=true;Topology.Dirty();
   Plugin.Info("Experimental unloaded network index ready: "+nodes.Count+" nodes, "+chests.Count+" storage records (eligibility checked on access).");
  }
  static void Added(ZDO zdo){if(Enabled&&bootstrapped)Record(zdo);}
  static void Record(ZDO z){
   if(z==null||!z.IsValid())return;int prefab=z.GetPrefab();
   if(nodeTypes.Contains(prefab)){
    long creator=z.GetLong(ZDOVars.s_creator,0);int schema=z.GetInt(NetworkMember.SchemaKey,0);if(creator==0||schema!=1)return;
    var stamp=(z.GetPosition(),z.GetString(NetworkMember.NetworkKey,""),creator,schema);
    if(nodeState.TryGetValue(z.m_uid,out var old)&&old.Equals(stamp))return;
    nodeState[z.m_uid]=stamp;nodes[z.m_uid]=z;UpdateReplica(z);structure++;if(Demand)Enqueue(z.m_uid);Topology.Dirty();
   }
   else if(chestTypes.Contains(prefab)){
    chests[z.m_uid]=z;UpdateReplica(z);var sector=ZoneSystem.GetZone(z.GetPosition());long creator=z.GetLong(ZDOVars.s_creator,0);
    if(positions.TryGetValue(z.m_uid,out var prior)&&prior==sector&&creators.TryGetValue(z.m_uid,out var owner)&&owner==creator)return;
    creators[z.m_uid]=creator;
    if(positions.TryGetValue(z.m_uid,out var old)&&old!=sector&&chestSectors.TryGetValue(old,out var previous))previous.Remove(z.m_uid);
    positions[z.m_uid]=sector;if(!chestSectors.TryGetValue(sector,out var list))chestSectors[sector]=list=new HashSet<ZDOID>();list.Add(z.m_uid);structure++;Topology.Dirty();
   }else if(wardTypes.Contains(prefab)){wards[z.m_uid]=z;wardAccess.Clear();structure++;Topology.Dirty();}
  }
  static void UpdateReplica(ZDO z){
   if(!replicas.TryGetValue(z.m_uid,out var go)||!go)return;
   go.transform.position=z.GetPosition();go.transform.rotation=z.GetRotation();R.Set(go.GetComponent<Piece>(),"m_creator",z.GetLong(ZDOVars.s_creator,0));
  }
  static void Changed(ZDO __instance){
   if(!Enabled||!bootstrapped)return;var id=__instance.m_uid;
   if(nodeTypes.Contains(__instance.GetPrefab()))Record(__instance);
   else if(wards.ContainsKey(id)){wardAccess.Clear();structure++;Topology.Dirty();}
   else if(chests.ContainsKey(id)){if(!creators.TryGetValue(id,out var owner)||owner!=__instance.GetLong(ZDOVars.s_creator,0))Record(__instance);StorageIndex.Changed(R.Key(id));}
  }
  static void Destroyed(ZDOID uid){
   if(!Enabled)return;bool changed=nodes.Remove(uid)|chests.Remove(uid)|wards.Remove(uid);if(!changed)return;
   if(positions.TryGetValue(uid,out var sector)){if(chestSectors.TryGetValue(sector,out var ids))ids.Remove(uid);positions.Remove(uid);}
   requestedChests.Remove(uid);queued.Remove(uid);nodeState.Remove(uid);creators.Remove(uid);wardAccess.Clear();
   if(replicas.TryGetValue(uid,out var go)){replicas.Remove(uid);if(go){var core=go.GetComponent<Core>();if(core)core.Detach();R.Set(go.GetComponent<ZNetView>(),"m_zdo",null);UnityEngine.Object.Destroy(go);}}
   StorageIndex.Changed(R.Key(uid));structure++;Topology.Dirty();
  }
  static void Loaded(ZNetView __instance){
   if(!Enabled||!R.Valid(__instance))return;var z=__instance.GetZDO();
   if(nodeTypes.Contains(z.GetPrefab())||chestTypes.Contains(z.GetPrefab())||wardTypes.Contains(z.GetPrefab())){Record(z);Topology.Dirty();}
   else if(__instance.GetComponent<Container>())Topology.Dirty();
  }
  static void Unloading(ZNetView __instance){
   if(!Enabled||!R.Valid(__instance))return;var id=__instance.GetZDO().m_uid;
   if(requestedChests.Contains(id)){Enqueue(id);StorageIndex.Changed(R.Key(id));}
   if(nodes.ContainsKey(id)||chests.ContainsKey(id)||__instance.GetComponent<Container>())Topology.Dirty();
  }
  static void PinTransactions(List<ZDO> currentNearObjects){
   if(!Enabled||Transport.Leases.Count==0)return;
   // A real instance which already holds a lease must live until release; only
   // the few participating chests are pinned, never the surrounding area.
   foreach(var lease in Transport.Leases.Values)if(lease.Container&&!IsReplica(lease.Container)&&R.Valid(R.View(lease.Container))){var z=R.View(lease.Container).GetZDO();if(!currentNearObjects.Contains(z))currentNearObjects.Add(z);}
  }
  static void Enqueue(ZDOID id){if(!replicas.ContainsKey(id)&&queued.Add(id))queue.Enqueue(id);}
  internal static bool Prepare(){
   if(!Enabled)return true;demandUntil=Time.unscaledTime+.25f;
   if(!bootstrapped)return false;
   if(nodesRequested!=structure){nodesRequested=structure;foreach(var id in nodes.Keys)if(!replicas.ContainsKey(id))Enqueue(id);}
   return !queued.Any(id=>nodes.ContainsKey(id));
  }
  internal static void Request(Core core){
   if(!Enabled||!core)return;demandUntil=Time.unscaledTime+.25f;
   var member=core.GetComponent<NetworkMember>();string network=member.Network;
   if(requestedNetworks.TryGetValue(network,out long revision)&&revision==structure)return;
   requestedNetworks[network]=structure;
   foreach(var node in Topology.Graph.Nodes.Values.Where(n=>n.Network==network&&Topology.Graph.Hops.ContainsKey(n.Id))){
    var center=ZoneSystem.GetZone(new Vector3((float)node.Position.X,(float)node.Position.Y,(float)node.Position.Z));int radius=Mathf.CeilToInt((float)node.Storage/64)+1;
    for(int x=-radius;x<=radius;x++)for(int y=-radius;y<=radius;y++)if(chestSectors.TryGetValue(new Vector2s(center.x+x,center.y+y),out var ids))foreach(var id in ids){
     var z=chests[id];var prefab=RemoteContext.Prefab(z);if(!prefab||!ContainerPolicy.Eligible(prefab.name)||z.GetLong(ZDOVars.s_creator,0)==0||node.Position.Distance2(Topology.Position(z.GetPosition()))>node.Storage*node.Storage)continue;
     requestedChests.Add(id);
     // Retained live instances also need a single-player owner after their zone
     // unloads. Keep their real inventory; an offline clone would duplicate it.
     if(z.GetOwner()!=ZNet.GetUID())z.SetOwner(ZNet.GetUID());
     if(!ZNetScene.instance.FindInstance(id))Enqueue(id);
    }
   }
  }
  internal static void SettingsChanged(){if(!Enabled)return;structure++;requestedNetworks.Clear();wardAccess.Clear();}
  internal static void Tick(){
   if(!Demand||queue.Count==0)return;var clock=Stopwatch.StartNew();int budget=8;
   while(queue.Count>0&&budget-->0){var id=queue.Dequeue();queued.Remove(id);if(!replicas.ContainsKey(id)){
     var z=RemoteContext.Data(id);if(z!=null&&(nodes.ContainsKey(id)||requestedChests.Contains(id)))try{Create(z);}catch(Exception e){Plugin.Error("experimental adapter "+R.Key(id),e);}
    }if(clock.Elapsed.TotalMilliseconds>=.75)break;}
   if(queue.Count==0)Topology.Dirty();
  }
  static void Create(ZDO z){
   var prefab=RemoteContext.Prefab(z);if(!prefab||z.GetLong(ZDOVars.s_creator,0)==0)return;
   bool node=nodes.ContainsKey(z.m_uid);if(!node&&(!ContainerPolicy.Eligible(prefab.name)||!chestTypes.Contains(z.GetPrefab())))return;
   var go=new GameObject(prefab.name);go.SetActive(false);go.transform.position=z.GetPosition();go.transform.rotation=z.GetRotation();
   try{
    var replica=go.AddComponent<UnloadedReplica>();replica.Data=z;
    var view=go.AddComponent<ZNetView>();R.Set(view,"m_zdo",z);
    var piece=go.AddComponent<Piece>();R.Set(piece,"m_nview",view);R.Set(piece,"m_creator",z.GetLong(ZDOVars.s_creator,0));
    if(node){
     var member=go.AddComponent<NetworkMember>();member.View=view;
     if(prefab.GetComponent<Core>()){var core=go.AddComponent<Core>();R.Set(core,"view",view);Core.Live.Add(core);}
    }else{
     var original=prefab.GetComponent<Container>();if(!original||original.m_privacy!=Container.PrivacySetting.Public)throw new InvalidOperationException("unsupported offline container");
     var c=go.AddComponent<Container>();c.m_name=original.m_name;c.m_width=original.m_width;c.m_height=original.m_height;c.m_privacy=original.m_privacy;c.m_bkg=original.m_bkg;
     R.Set(c,"m_nview",view);R.Set(c,"m_piece",piece);R.Set(c,"m_inventory",new Inventory(c.m_name,c.m_bkg,c.m_width,c.m_height));
     if(z.GetOwner()!=ZNet.GetUID())z.SetOwner(ZNet.GetUID());
    }
    replicas.Add(z.m_uid,go);
   }catch{R.Set(go.GetComponent<ZNetView>(),"m_zdo",null);UnityEngine.Object.Destroy(go);throw;}
  }
  internal static bool IsReplica(Component c)=>Enabled&&c&&c.GetComponent<UnloadedReplica>()&&R.Valid(R.View(c));
  // A refused, unpaid write changed only the temporary inventory. Discard it
  // instead of overwriting newer saved data or holding the chest forever.
  internal static bool DiscardUnpaid(Container c,bool paid){
   if(!IsReplica(c)||paid)return false;var r=c.GetComponent<UnloadedReplica>();if(r.SavedAfterLoad)return false;
   r.Read=false;StorageIndex.Changed(R.Key(r.Data.m_uid));return true;
  }
  internal static void Released(Container c){if(IsReplica(c)){StorageIndex.Changed(R.Key(R.View(c).GetZDO().m_uid));Topology.Dirty();}}
  static bool ReadFailure(Container c,Exception e){
   if(!IsReplica(c))return false;var r=c.GetComponent<UnloadedReplica>();
   if(!r.ReportedFailure||r.FailedRevision!=r.Data.DataRevision){r.ReportedFailure=true;r.FailedRevision=r.Data.DataRevision;Plugin.Error("experimental inventory unavailable "+R.Key(r.Data.m_uid),e);}return true;
  }
  internal static bool CanUseUnloaded(Container c){
   if(!Enabled||!c||!R.Valid(R.View(c))||c.GetInventory()==null)return false;
   var z=R.View(c).GetZDO();
   if(!chests.TryGetValue(z.m_uid,out var saved)||!ReferenceEquals(saved,z)||!ReferenceEquals(RemoteContext.Data(z.m_uid),z))return false;
   // A live Container may outlast its zone (including containers retained by
   // another mod). Area readiness describes the entire zone, not this source.
   // Access/OwnerSource still enforce eligibility, placement, wards and leases.
   return IsReplica(c)||(ZNetScene.instance&&ZNetScene.instance.FindInstance(z.m_uid)==c.gameObject);
  }
  internal static bool SourceReady(Container c)=>CanUseUnloaded(c)||(c&&ZNetScene.instance&&ZNetScene.instance.IsAreaReady(c.transform.position));
  internal static bool KeepOwner(ZDO z,long owner)=>Enabled&&owner==0&&requestedChests.Contains(z.m_uid)&&chests.TryGetValue(z.m_uid,out var saved)&&ReferenceEquals(saved,z);
  internal static Core FindCore(ZDOID id){if(Enabled&&replicas.TryGetValue(id,out var go)&&go)return go.GetComponent<Core>();return null;}
  internal static Container FindContainer(ZDOID id){
   Container proxy=null;if(Enabled&&replicas.TryGetValue(id,out var go)&&go)proxy=go.GetComponent<Container>();
   if(proxy&&Transport.Leases.ContainsKey(proxy.GetInventory()))return proxy;
   var live=ZNetScene.instance?ZNetScene.instance.FindInstance(id)?.GetComponent<Container>():null;
   return live&&R.Valid(R.View(live))?live:proxy;
  }
  internal static bool Ward(Vector3 point,long actor){
   if(wardAccess.TryGetValue((point,actor),out bool cached))return cached;
   bool denied=false,allowed=false;
   foreach(var z in wards.Values){if(!z.IsValid()||!z.GetBool(ZDOVars.s_enabled,false))continue;var prefab=RemoteContext.Prefab(z);var ward=prefab?prefab.GetComponent<PrivateArea>():null;if(!ward)continue;
    var delta=z.GetPosition()-point;if(delta.x*delta.x+delta.z*delta.z>=ward.m_radius*ward.m_radius)continue;
    bool permitted=z.GetLong(ZDOVars.s_creator,0)==actor;int count=z.GetInt(ZDOVars.s_permitted,0);for(int i=0;i<count&&!permitted;i++)permitted=z.GetLong("pu_id"+i,0)==actor;
    if(permitted)allowed=true;else denied=true;
   }if(wardAccess.Count>=512)wardAccess.Clear();return wardAccess[(point,actor)]=allowed||!denied;
  }
  static bool LoadReplica(Container __instance,ref bool __result){
   if(!IsReplica(__instance))return true;__result=false;var r=__instance.GetComponent<UnloadedReplica>();
   if(Transport.Leases.ContainsKey(__instance.GetInventory())||__instance.IsInUse())return false;
   r.SavedAfterLoad=false;
   var bytes=r.Data.GetByteArray(ZDOVars.s_items)??Array.Empty<byte>();
   if(r.Read&&bytes.SequenceEqual(r.Expected))return false;
   r.Read=false;var inventory=__instance.GetInventory();
   // Never silently drop unknown or altered item records on an offline rewrite.
   if(bytes.Length>0){inventory.Load(new ZPackage(bytes));var roundTrip=new ZPackage();inventory.Save(roundTrip);if(!bytes.SequenceEqual(roundTrip.GetArray())){inventory.RemoveAll();r.Read=false;throw new InvalidOperationException("Offline inventory cannot round-trip unchanged; visit this chest before using it remotely.");}}
   else inventory.RemoveAll();
   r.Expected=(byte[])bytes.Clone();r.Read=true;r.ReportedFailure=false;R.Set(__instance,"m_lastRevision",r.Data.DataRevision);__result=true;return false;
  }
  static bool SaveReplica(Container __instance){
   if(!IsReplica(__instance))return true;var r=__instance.GetComponent<UnloadedReplica>();
   var current=r.Data.GetByteArray(ZDOVars.s_items)??Array.Empty<byte>();
   if(!r.Read||!current.SequenceEqual(r.Expected))throw new InvalidOperationException("Offline inventory changed before save; refusing stale overwrite.");
   var package=new ZPackage();__instance.GetInventory().Save(package);var bytes=package.GetArray();r.Data.Set(ZDOVars.s_items,bytes);r.SavedAfterLoad=true;r.Expected=(byte[])bytes.Clone();R.Set(__instance,"m_lastRevision",r.Data.DataRevision);return false;
  }
 }
}
