using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RunicStorageNetwork.API;
using RunicStorageNetwork.Logic;

namespace RunicStorageNetwork {
 internal static partial class UnloadedNetworks {
  internal static bool ApiCatalogReady=>Enabled&&Authority&&bootstrapped;
  internal static IEnumerable<ZDO> ApiNodes=>nodes.Values;
  internal static IEnumerable<ZDO> ApiChests=>chests.Values;
  internal static IEnumerable<ZDO> ApiCandidates(NetworkGraph graph,string network){
   var seen=new HashSet<ZDOID>();
   foreach(var node in graph.Nodes.Values.Where(n=>n.Network==network&&graph.Hops.ContainsKey(n.Id))){
    var center=ZoneSystem.GetZone(new Vector3((float)node.Position.X,(float)node.Position.Y,(float)node.Position.Z));int radius=Mathf.CeilToInt((float)node.Storage/64);
    for(int x=-radius-1;x<=radius+1;x++)for(int y=-radius-1;y<=radius+1;y++)if(chestSectors.TryGetValue(new Vector2s(center.x+x,center.y+y),out var ids))
     foreach(var id in ids)if(chests.TryGetValue(id,out var z)&&node.Position.Distance2(Topology.Position(z.GetPosition()))<=node.Storage*node.Storage&&seen.Add(id))yield return z;
   }
  }
  internal static void ApiRefreshAccess()=>wardAccess.Clear();
  internal static Container ApiContainer(ZDO z,out bool created){
   created=false;var live=ZNetScene.instance.FindInstance(z.m_uid)?.GetComponent<Container>();
   if(live&&R.Valid(R.View(live)))return live;
   if(!replicas.ContainsKey(z.m_uid)){Create(z);created=true;}
   return replicas.TryGetValue(z.m_uid,out var go)&&go?go.GetComponent<Container>():null;
  }
  internal static void ApiReleaseAdapter(ZDOID id){
   if(requestedChests.Contains(id)||!replicas.TryGetValue(id,out var go)||!go||Transport.Reserved(RemoteContext.Data(id)))return;
   var c=go.GetComponent<Container>();if(c&&c.IsInUse())return;
   replicas.Remove(id);R.Set(go.GetComponent<ZNetView>(),"m_zdo",null);UnityEngine.Object.Destroy(go);Topology.Dirty();
  }
 }
 internal static class ApiWorld {
  internal static readonly Dictionary<ZDOID,CachedInventory> InventoryCache=new Dictionary<ZDOID,CachedInventory>();
  internal sealed class CachedInventory {internal uint Revision;internal long Owner;internal List<Stock> Stock;internal double Used,Next;internal int Bytes;internal bool Ready;}
  static int cacheBytes;
  internal static ApiStatus Context(ApiContext context,long sender,bool receipt,out ZDO z,out long creator){
   z=null;creator=0;if(context==null||!ApiRules.Text(context.ModId)||context.ConsumerId==ZDOID.None)return ApiStatus.InvalidRequest;
   z=RemoteContext.Data(context.ConsumerId);var prefab=RemoteContext.Prefab(z);
   var marker=prefab?prefab.GetComponent<NetworkConsumer>():null;
   if(z==null||!prefab||!prefab.GetComponent<Piece>()||!prefab.GetComponent<ZNetView>()||!marker||marker.ModId!=context.ModId)return ApiStatus.UnsupportedConsumer;
   creator=z.GetLong(ZDOVars.s_creator,0);if(creator==0||prefab.GetComponent<Rigidbody>()||prefab.GetComponent<Ship>())return ApiStatus.UnsupportedConsumer;
   if(z.GetOwner()!=sender&&!(receipt&&sender==ZNet.GetUID()&&ZNet.instance.IsServer()))return ApiStatus.NotOwner;
   return ApiStatus.Ready;
  }
  internal static bool Eligible(ZDO z,long creator){
   var prefab=RemoteContext.Prefab(z);var c=prefab?prefab.GetComponent<Container>():null;
   return z!=null&&z.IsValid()&&c&&ContainerPolicy.Eligible(prefab.name)&&z.GetLong(ZDOVars.s_creator,0)!=0&&c.m_privacy==Container.PrivacySetting.Public&&!c.m_wagon&&!c.m_rootObjectOverride&&!prefab.GetComponent<Rigidbody>()&&!prefab.GetComponent<Ship>()&&UnloadedNetworks.Ward(z.GetPosition(),creator);
  }
  internal static Inventory Decode(ZDO z,byte[] bytes){
   if(bytes.Length>ApiRules.MaxInventoryBytes)throw new InvalidOperationException("API inventory exceeds byte limit");
   var prefab=RemoteContext.Prefab(z);var source=prefab?prefab.GetComponent<Container>():null;if(!source)throw new InvalidOperationException("No container");
   var inv=new Inventory(source.m_name,source.m_bkg,source.m_width,source.m_height);
   if(bytes.Length>0){inv.Load(new ZPackage(bytes));var check=new ZPackage();inv.Save(check);if(!bytes.SequenceEqual(check.GetArray()))throw new InvalidOperationException("Inventory does not round-trip without loss");}
   return inv;
  }
  internal static List<Stock> Read(ZDO z,double now,bool force=false){
   if(InventoryCache.TryGetValue(z.m_uid,out var hit)){
    hit.Used=now;if(!force&&hit.Revision==z.DataRevision&&hit.Owner==z.GetOwner()&&now<hit.Next)return hit.Ready?hit.Stock:null;
    cacheBytes-=hit.Bytes;
   }else {if(InventoryCache.Count>=8192)Trim(now,true);hit=new CachedInventory();InventoryCache[z.m_uid]=hit;}
   hit.Revision=z.DataRevision;hit.Owner=z.GetOwner();hit.Used=now;hit.Next=now+30;hit.Ready=false;hit.Bytes=0;
   try{
    var live=ZNetScene.instance.FindInstance(z.m_uid)?.GetComponent<Container>();
    if(live&&R.View(live).IsOwner()&&!Transport.Locked(live.GetInventory())&&!Integrations.IsBusy(live.GetInventory())&&!live.IsInUse())hit.Stock=Stockroom.Snapshot(live.GetInventory(),R.Key(z.m_uid),null,true);
    else hit.Stock=Stockroom.Snapshot(Decode(z,z.GetByteArray(ZDOVars.s_items)??Array.Empty<byte>()),R.Key(z.m_uid),null,true);
    hit.Bytes=hit.Stock.Sum(s=>96+s.Item.Length*2);if(hit.Bytes>1024*1024)throw new InvalidOperationException("Inventory index exceeds byte limit");
    cacheBytes+=hit.Bytes;hit.Ready=true;if(cacheBytes>12*1024*1024)Trim(now,true);return hit.Stock;
   }catch{hit.Stock=null;hit.Bytes=0;hit.Next=now+2;return null;}
  }
  internal static void Invalidate(ZDOID id){if(InventoryCache.TryGetValue(id,out var e))e.Next=0;}
  internal static void Trim(double now,bool pressure=false){
   foreach(var pair in InventoryCache.OrderBy(p=>p.Value.Used).ToArray()){
    if(now-pair.Value.Used<30&&(!pressure||cacheBytes<10*1024*1024&&InventoryCache.Count<8000))break;
    cacheBytes-=pair.Value.Bytes;InventoryCache.Remove(pair.Key);
   }
  }
  internal static void Clear(){InventoryCache.Clear();cacheBytes=0;}
 }
 internal sealed class ApiNetworkView {
  internal long Creator,Structure=-1,Revision;internal NetworkGraph Graph;internal Point Point;internal double Used,NextScan,ObservedAt;
  internal bool Scanning,Complete,Limited;internal int Unknown,Bytes;internal string Network;
  internal readonly Dictionary<string,List<Stock>> Sources=new Dictionary<string,List<Stock>>();
  internal readonly Dictionary<ResourceKey,long> Amounts=new Dictionary<ResourceKey,long>();
  internal IReadOnlyList<ResourceAmount> Resources=Array.AsReadOnly(Array.Empty<ResourceAmount>());
  IEnumerator<int> scan;int contentHash;long indexRevision;
  readonly ResourceCatalog index=new ResourceCatalog();
  internal void Repair(){scan?.Dispose();scan=null;Scanning=false;Structure=-1;NextScan=0;UnloadedNetworks.ApiRefreshAccess();foreach(var key in Sources.Keys){var z=RemoteContext.Source(key);if(z!=null)ApiWorld.Invalidate(z.m_uid);}}
  internal void Tick(double now){
   Used=Math.Max(Used,0);
   if(scan==null&&now>=NextScan){scan=Scan(now).GetEnumerator();Scanning=true;}
   if(scan==null)return;
   try{if(!scan.MoveNext()){scan.Dispose();scan=null;Scanning=false;NextScan=now+1;}}
   catch(InvalidOperationException){scan?.Dispose();scan=null;Scanning=false;Complete=false;NextScan=now+.25;}
  }
  IEnumerable<int> Scan(double now){
   long version=UnloadedNetworks.CatalogRevision;
   if(Graph==null||Structure!=version){
    Graph=GatewayRuntime.Graph(Creator);Network=Graph.Choose(Point,n=>true);Structure=UnloadedNetworks.CatalogRevision;yield return 0;
   }
   var next=new Dictionary<string,List<Stock>>(StringComparer.Ordinal);int unknown=0,bytes=0;Limited=false;
   foreach(var z in UnloadedNetworks.ApiCandidates(Graph,Network)){
    if(Structure!=UnloadedNetworks.CatalogRevision){Complete=false;yield break;}
    if(ApiWorld.Eligible(z,Creator)&&Graph.Covers(Network,Topology.Position(z.GetPosition()),n=>true)){
     var stocks=ApiWorld.Read(z,now);if(stocks==null)unknown++;else {
      var filtered=GatewayRuntime.Filter(Graph,Network,z.GetPosition(),new Vector3((float)Point.X,(float)Point.Y,(float)Point.Z),stocks);
      if(!ReferenceEquals(filtered,stocks))stocks=filtered.ToList();
      bytes+=64+stocks.Sum(s=>192+s.Item.Length*4);if(bytes>1024*1024||!ApiRuntime.AllowViewBytes(this,bytes)){Limited=true;Complete=false;Sources.Clear();index.Clear();Amounts.Clear();Resources=Array.AsReadOnly(Array.Empty<ResourceAmount>());Bytes=0;ObservedAt=now;yield break;}next[R.Key(z.m_uid)]=stocks;
     }
    }yield return 0;
   }
   foreach(var key in Sources.Keys.Where(k=>!next.ContainsKey(k)).ToArray())index.Remove(key);
   foreach(var pair in next){if(!Sources.TryGetValue(pair.Key,out var previous)||!ReferenceEquals(previous,pair.Value))index.Replace(pair.Key,1,++indexRevision,pair.Value);yield return 0;}
   Sources.Clear();foreach(var pair in next)Sources[pair.Key]=pair.Value;Bytes=bytes;
   var amounts=new Dictionary<ResourceKey,long>();
   foreach(var list in next.Values){foreach(var stock in list){var key=new ResourceKey(stock.Item,stock.Quality);amounts.TryGetValue(key,out long old);amounts[key]=checked(old+stock.Amount);}yield return 0;}
   int hash=17;unchecked{foreach(var pair in amounts.OrderBy(x=>x.Key.PrefabName,StringComparer.Ordinal).ThenBy(x=>x.Key.Quality))hash=hash*31+pair.Key.GetHashCode()+pair.Value.GetHashCode();}
   if(hash!=contentHash||unknown!=Unknown||!Complete||amounts.Count!=Amounts.Count||amounts.Any(p=>!Amounts.TryGetValue(p.Key,out long old)||old!=p.Value)){contentHash=hash;Amounts.Clear();foreach(var pair in amounts)Amounts[pair.Key]=pair.Value;Resources=Array.AsReadOnly(amounts.Select(x=>new ResourceAmount(x.Key,x.Value)).OrderBy(x=>x.Resource.PrefabName,StringComparer.Ordinal).ThenBy(x=>x.Resource.Quality).ToArray());Revision++;}
   Unknown=unknown;Complete=unknown==0;ObservedAt=now;
  }
  internal List<Stock> Stocks(IEnumerable<Need> needs)=>index.Find(needs.Select(n=>n.Item));
 }
}
