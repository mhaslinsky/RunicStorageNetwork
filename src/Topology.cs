using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RunicStorageNetwork.Logic;

namespace RunicStorageNetwork {
 public class NetworkMember:MonoBehaviour {
  internal const string NetworkKey="rsn_network",ChoiceKey="rsn_choice",SchemaKey="rsn_schema";
  internal ZNetView View;
  internal bool Root=>GetComponent<Core>();
  internal bool Valid=>!destroyed&&R.Valid(View)&&GetComponent<Piece>()&&GetComponent<Piece>().IsPlacedByPlayer();
  internal string Id=>R.Key(View.GetZDO().m_uid);
  internal string SavedNetwork=>Valid?View.GetZDO().GetString(NetworkKey,""):"";
  internal string Network=>Valid&&Topology.Graph.Nodes.TryGetValue(Id,out var node)?node.Network:SavedNetwork;
  internal bool Choice=>Valid&&View.GetZDO().GetBool(ChoiceKey,false);
  bool rpcRegistered,destroyed;string observed,state;WearNTear wear;
  protected virtual void Awake(){View=GetComponent<ZNetView>();}
  protected virtual void Update(){
   if(!Valid){if(Topology.Members.Remove(this))Topology.Dirty();return;}
   if(!rpcRegistered){wear=GetComponent<WearNTear>();if(wear)wear.m_onDestroyed+=ConfirmedDestruction;rpcRegistered=true;}
   if(Topology.Members.Add(this))Topology.Dirty();
   var z=View.GetZDO();
   if(View.IsOwner()&&(z.GetInt(SchemaKey,0)==0||(Root&&SavedNetwork==""))){
    if(Root)z.Set(NetworkKey,NetworkGraph.PersistentIdentity(z.GetString(NetworkKey,"")));
    z.Set(SchemaKey,1);Topology.Dirty();Plugin.Debug("node schema=1 NodeId="+Id+" NetworkId="+Network);
   }
   string current=SavedNetwork+":"+z.GetInt(SchemaKey,0);if(GatewayRuntime.Is(z))current+=":"+z.GetString(Gateway.TagKey,"")+":"+z.GetString(Gateway.BindingKey,"");
   if(current!=observed){observed=current;Topology.Dirty();Plugin.Debug("node identity NodeId="+Id+" NetworkId="+Network);}
  }
  internal void LogState(string next){if(next==state)return;Plugin.Debug("node state NodeId="+Id+" NetworkId="+Network+" "+state+" -> "+next);state=next;}
  void ConfirmedDestruction(){if(Root&&Network!="")Topology.DestroyedRoots.Add(Network);Plugin.Debug("confirmed destruction NodeId="+Id+" NetworkId="+Network);destroyed=true;Topology.Members.Remove(this);Topology.Dirty();}
  protected virtual void OnDestroy(){if(wear)wear.m_onDestroyed-=ConfirmedDestruction;if(Topology.Members.Remove(this))Topology.Dirty();}

 }
 internal static class Topology {
  internal static readonly HashSet<NetworkMember> Members=new HashSet<NetworkMember>();
  internal static readonly HashSet<string> DestroyedRoots=new HashSet<string>(StringComparer.Ordinal);
  internal static NetworkGraph Graph=new NetworkGraph(new NetworkNode[0],50);
  static readonly Dictionary<string,NetworkMember> byId=new Dictionary<string,NetworkMember>(StringComparer.Ordinal);
  static readonly Dictionary<string,List<Container>> pools=new Dictionary<string,List<Container>>(StringComparer.Ordinal);
  static readonly Dictionary<Container,List<string>> containerNetworks=new Dictionary<Container,List<string>>();
  static readonly Dictionary<string,Core> labelRoots=new Dictionary<string,Core>(StringComparer.Ordinal);
  internal static IEnumerable<string> ContainerNetworks(Container c)=>containerNetworks.TryGetValue(c,out var networks)?networks:(IEnumerable<string>)Array.Empty<string>();
  internal static Core LabelRootSnapshot(string network)=>network!=null&&labelRoots.TryGetValue(network,out var root)&&root&&root.Valid?root:null;
  static readonly Dictionary<long,NetworkGraph> actorGraphs=new Dictionary<long,NetworkGraph>();
  sealed class Selection {internal Vector3 Point;internal float Until;internal Core Core;}
  static readonly Dictionary<long,Selection> selections=new Dictionary<long,Selection>();
  static float next;static bool dirty=true,refreshing;static ulong accessRevision;
  internal static int DisplayRevision {get;private set;}
  internal static Point Position(Vector3 p)=>new Point(p.x,p.y,p.z);
  internal static void Dirty(){dirty=true;unchecked{DisplayRevision++;}selections.Clear();actorGraphs.Clear();foreach(var core in Core.Live)if(core)core.Invalidate();}
  internal static void CheckAccessRevision(){
   ulong stamp=1469598103934665603UL;
   foreach(var area in R.Get<List<PrivateArea>>(typeof(PrivateArea),"m_allAreas")){
    var view=R.View(area);if(!R.Valid(view))continue;var z=view.GetZDO();
    unchecked{stamp=(stamp^(ulong)z.m_uid.GetHashCode())*1099511628211UL;stamp=(stamp^z.DataRevision)*1099511628211UL;}
   }
   if(stamp==accessRevision)return;accessRevision=stamp;unchecked{DisplayRevision++;}selections.Clear();actorGraphs.Clear();foreach(var core in Core.Live)if(core)core.Invalidate();
  }
  internal static void Clear(){GatewayRuntime.Clear();Members.Clear();DestroyedRoots.Clear();byId.Clear();pools.Clear();containerNetworks.Clear();labelRoots.Clear();selections.Clear();actorGraphs.Clear();Graph=new NetworkGraph(new NetworkNode[0],50);next=0;dirty=true;HoverInfo.Clear();ContainerHover.Clear();}
  internal static NetworkMember Member(string id)=>byId.TryGetValue(id,out var n)&&n&&n.Valid?n:null;
  internal static ZDOID[] OperationNodes(string network){
   if(!UnloadedNetworks.Enabled)return Members.Where(m=>m&&m.Valid&&m.Network==network).Select(m=>m.View.GetZDO().m_uid).Distinct().ToArray();
   return Graph.Nodes.Values.Where(n=>n.Network==network).Select(n=>Member(n.Id)).Where(m=>m&&m.Valid).Select(m=>m.View.GetZDO().m_uid).Distinct().ToArray();
  }
  internal static Core Root(string network){Refresh();return RootSnapshot(network);}
  internal static Core RootSnapshot(string network)=>network!=null&&Graph.Roots.TryGetValue(network,out var id)?Member(id)?.GetComponent<Core>():null;
  internal static bool Allowed(NetworkNode node,long actor){var n=Member(node.Id);return n&&n.Valid&&(actor==0||Access.Ward(n.transform.position,actor));}
  internal static bool NetworkAllowed(string network,long actor)=>Graph.Roots.TryGetValue(network,out var id)&&Allowed(Graph.Nodes[id],actor);
  internal static void Refresh(bool force=false){
   if(refreshing||!ZNetScene.instance||!ZoneSystem.instance)return;
   if(UnloadedNetworks.Enabled&&!UnloadedNetworks.Prepare())return;
   if(!force&&!dirty&&(UnloadedNetworks.Enabled||Time.unscaledTime<next))return;refreshing=true;
   try {
    dirty=false;next=Time.unscaledTime+Plugin.Rescan.Value;byId.Clear();selections.Clear();actorGraphs.Clear();
    foreach(var member in Members.Where(m=>m&&m.Valid&&(UnloadedNetworks.Enabled||!GatewayRuntime.Is(m.View.GetZDO()))).OrderBy(m=>m.Id,StringComparer.Ordinal))byId[member.Id]=member;
    if(UnloadedNetworks.Enabled)foreach(var member in UnloadedNetworks.Members)byId[member.Id]=member;
    Graph=UnloadedNetworks.Enabled?GatewayRuntime.Graph(0):NetworkGraph.Automatic(byId.Values.Select(m=>new NetworkNode{Id=m.Id,Network=m.SavedNetwork,Root=m.Root,Position=Position(m.transform.position),
     Confirmed=m.View.GetZDO().GetInt(NetworkMember.SchemaKey,0)==1&&(UnloadedNetworks.IsReplica(m)||ZNetScene.instance.IsAreaReady(m.transform.position)),Storage=m.Root?Plugin.StorageRadius.Value:Plugin.RelayStorage.Value,Supply=m.Root?Plugin.SupplyRadius.Value:Plugin.RelaySupply.Value}),Plugin.RelayLink.Value);
    pools.Clear();containerNetworks.Clear();labelRoots.Clear();
    // Persistent core identities keep the displayed name stable across world reloads.
    foreach(var root in Graph.Nodes.Values.Where(n=>n.Root&&Graph.Hops.ContainsKey(n.Id)&&Member(n.Id)).OrderBy(n=>Member(n.Id).SavedNetwork,StringComparer.Ordinal).ThenBy(n=>n.Id,StringComparer.Ordinal))
     if(!labelRoots.ContainsKey(root.Network))labelRoots[root.Network]=Member(root.Id).GetComponent<Core>();
    // One pass through loaded pieces, then spatial buckets shared by every node.
    float cellSize=Mathf.Max(Plugin.StorageRadius.Value,Plugin.RelayStorage.Value);
    Func<Vector3,(int,int,int)> cell=p=>((int)Math.Floor(p.x/cellSize),(int)Math.Floor(p.y/cellSize),(int)Math.Floor(p.z/cellSize));
    var chests=new Dictionary<(int,int,int),List<Container>>();
    var pieces=(IEnumerable<Piece>)R.Get<List<Piece>>(typeof(Piece),"s_allPieces");
    if(UnloadedNetworks.Enabled)pieces=pieces.Concat(UnloadedNetworks.Containers.Select(c=>c.GetComponent<Piece>()));
    var seenChests=UnloadedNetworks.Enabled?new HashSet<ZDOID>():null;
    foreach(var piece in pieces){
     // The component check rejects nearly every piece first; only real containers are named.
     var c=piece?piece.GetComponent<Container>():null;if(!c||!R.Valid(R.View(c)))continue;
     if(seenChests!=null){var id=R.View(c).GetZDO().m_uid;if(!seenChests.Add(id))continue;c=UnloadedNetworks.FindContainer(id);if(!c)continue;}
     if(!ContainerPolicy.Eligible(R.Id(c.gameObject)))continue;
     StorageIndex.Register(c);
     var key=cell(c.transform.position);if(!chests.TryGetValue(key,out var bucket))chests[key]=bucket=new List<Container>();bucket.Add(c);
    }
    foreach(var node in Graph.Nodes.Values.Where(n=>Graph.Hops.ContainsKey(n.Id))){
     if(!pools.TryGetValue(node.Network,out var pool))pools[node.Network]=pool=new List<Container>();
     var at=cell(new Vector3((float)node.Position.X,(float)node.Position.Y,(float)node.Position.Z));
     for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)if(chests.TryGetValue((at.Item1+x,at.Item2+y,at.Item3+z),out var bucket))
      foreach(var c in bucket)if(node.Position.Distance2(Position(c.transform.position))<=node.Storage*node.Storage)pool.Add(c);
    }
    foreach(string key in pools.Keys.ToArray())pools[key]=pools[key].GroupBy(c=>R.Key(R.View(c).GetZDO().m_uid),StringComparer.Ordinal).Select(g=>g.First()).OrderBy(c=>R.Key(R.View(c).GetZDO().m_uid),StringComparer.Ordinal).ToList();
    foreach(var pair in pools)foreach(var c in pair.Value){
     if(!containerNetworks.TryGetValue(c,out var networks))containerNetworks[c]=networks=new List<string>();
     networks.Add(pair.Key);
    }
    foreach(var core in Core.Live)if(core)core.Scan();
    foreach(var member in byId.Values)member.LogState(State(member));
   }finally{refreshing=false;}
  }
  internal static List<Container> Pool(Core core){Refresh();var n=core?core.GetComponent<NetworkMember>():null;return n&&pools.TryGetValue(n.Network,out var list)?list:new List<Container>();}
  internal static Core Choose(Vector3 point,long actor){
   if(UnloadedNetworks.Requested)UnloadedMultiplayer.Touch(point,actor);
   if(!Plugin.Enabled)return null;Refresh();if(selections.TryGetValue(actor,out var cached)&&cached.Point==point&&Time.unscaledTime<cached.Until&&cached.Core&&cached.Core.Valid){UnloadedNetworks.Request(cached.Core);return cached.Core;}
   var graph=ForActor(actor);string net=graph.Choose(Position(point),n=>true);var core=net!=null&&graph.Roots.TryGetValue(net,out var root)?Member(root)?.GetComponent<Core>():null;selections[actor]=new Selection{Point=point,Until=Time.unscaledTime+.25f,Core=core};UnloadedNetworks.Request(core);return core;
  }
  internal static NetworkGraph ForActor(long actor){
   if(UnloadedNetworks.Enabled)return GatewayRuntime.Graph(actor);
   if(!actorGraphs.TryGetValue(actor,out var graph))actorGraphs[actor]=graph=NetworkGraph.Automatic(Graph.Nodes.Values.Select(n=>new NetworkNode{Id=n.Id,Network=Member(n.Id)?.SavedNetwork??"",Root=n.Root,Confirmed=n.Confirmed&&Allowed(n,actor),Position=n.Position,Storage=n.Storage,Supply=n.Supply}),Plugin.RelayLink.Value);
   return graph;
  }
  internal static bool Covers(Core core,Vector3 point,long actor){
   Refresh();var member=core?core.GetComponent<NetworkMember>():null;
   var graph=ForActor(actor);return member&&graph.Nodes.TryGetValue(member.Id,out var root)&&root.Confirmed&&graph.Covers(root.Network,Position(point),n=>true);
  }
  internal static bool Supplies(Core core,Vector3 point,long actor){
   Refresh();var member=core?core.GetComponent<NetworkMember>():null;
   var graph=ForActor(actor);return member&&graph.Nodes.TryGetValue(member.Id,out var root)&&root.Confirmed&&graph.Supplies(root.Network,Position(point),n=>true);
  }
  internal static Dictionary<string,string> Candidates(NetworkMember relay,long actor){
   Refresh();return Graph.Candidates(Position(relay.transform.position),n=>n.Id!=relay.Id&&Allowed(n,actor)&&NetworkAllowed(n.Network,actor));
  }
  internal static string State(NetworkMember m){
   if(!Plugin.Enabled)return "$rsn_disabled";if(m.Network=="")return "$rsn_disconnected";
   if(Graph.Hops.ContainsKey(m.Id))return "$rsn_connected";
   return Graph.Roots.ContainsKey(m.Network)||DestroyedRoots.Contains(m.Network)?"$rsn_disconnected":"$rsn_unknown";
  }
  // The console keeps few lines; the mod log always receives the untruncated list.
  static string Excerpt(List<string> names)=>names.Count<=12?string.Join(", ",names.ToArray()):string.Join(", ",names.Take(12).ToArray())+", … (+"+(names.Count-12)+")";
  internal static string Short(string network)=>string.IsNullOrEmpty(network)?"—":network.Substring(Math.Max(0,network.Length-13));
  internal static void Diagnose(Terminal terminal){
   Refresh(true);var p=Player.m_localPlayer;if(!p){terminal.AddString("[RSN] "+RsnLocalization.Text("diag_no_player"));return;}
   var n=byId.Values.OrderBy(m=>(m.transform.position-p.transform.position).sqrMagnitude).FirstOrDefault();
   Action<string,string> log=(raw,translated)=>{terminal.AddString("[RSN] "+translated);Plugin.Info(raw);};
   if(!n){log("No confirmed local nodes",RsnLocalization.Text("diag_no_nodes"));return;}
   string hops=Graph.Hops.TryGetValue(n.Id,out var h)?h.ToString():"?";
   log("NodeId="+n.Id+" NetworkId="+n.Network+" state="+State(n)+" hops="+hops,RsnLocalization.Text("diag_node",n.Id,n.Network,Localization.instance.Localize(State(n)),hops));
   if(Graph.Neighbors.TryGetValue(n.Id,out var neighbors))log("neighbors="+string.Join(",",neighbors),RsnLocalization.Text("diag_neighbors",string.Join(", ",neighbors)));
   var path=new List<string>();string cursor=n.Id;while(Graph.Parent.TryGetValue(cursor,out var parent)){path.Add(parent);cursor=parent;}
   log("path="+string.Join(" -> ",path),RsnLocalization.Text("diag_path",string.Join(" → ",path)));
   ContainerPolicy.Ensure();
   log("container policy supported="+ContainerPolicy.Supported+" excluded="+ContainerPolicy.Excluded.Count+"; "+ContainerPolicy.Rules.Summary,RsnLocalization.Text("diag_policy",ContainerPolicy.Supported,ContainerPolicy.Excluded.Count));
   if(ContainerPolicy.Excluded.Count>0)log("excluded="+string.Join(",",ContainerPolicy.Excluded.ToArray()),RsnLocalization.Text("diag_excluded",Excerpt(ContainerPolicy.Excluded)));
   var core=Root(n.Network);if(!core)return;core.Scan();log("candidate pool="+core.Pool.Count,RsnLocalization.Text("diag_pool",core.Pool.Count));
   foreach(var c in core.Pool){bool allowed=Access.Container(c,p.GetPlayerID(),core,out string reason);string id=R.Key(R.View(c).GetZDO().m_uid);log("ContainerId="+id+" "+(allowed?"available":reason),RsnLocalization.Text("diag_chest",id,RsnLocalization.Reason(allowed?"available":reason)));}
  }
 }
 internal sealed class NetworkSystem:MonoBehaviour {
  ZNet world;
  void Update(){
   if(world!=ZNet.instance){Topology.Clear();StorageIndex.Clear();world=ZNet.instance;}
   if(!world||!ZNetScene.instance)return;
   if(UnloadedNetworks.Requested)UnloadedMultiplayer.Tick();
   if(UnloadedNetworks.Enabled)UnloadedNetworks.Tick();else {Topology.CheckAccessRevision();Topology.Refresh();}

   StorageIndex.Tick();RecipeIndex.Background();
   HoverInfo.Tick();
   ContainerHover.Tick();
  }
  void OnDestroy(){Topology.Clear();StorageIndex.Clear();}
 }
}
