using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicStorageNetwork.Logic {
 public sealed partial class NetworkGraph {
  public readonly Dictionary<string,string> GatewayPairs=new Dictionary<string,string>(StringComparer.Ordinal);
  public readonly Dictionary<string,string> GatewayBindings=new Dictionary<string,string>(StringComparer.Ordinal);
  public readonly Dictionary<string,string> GatewayStates=new Dictionary<string,string>(StringComparer.Ordinal);
  readonly Dictionary<string,string> localComponents=new Dictionary<string,string>(StringComparer.Ordinal);
  readonly Dictionary<Point,HashSet<string>> supplyComponents=new Dictionary<Point,HashSet<string>>();
  readonly Dictionary<(string,Point,Point),int> routes=new Dictionary<(string,Point,Point),int>();
  public bool HasGateways=>GatewayStates.Count>0;
  sealed class Islands {
   readonly Dictionary<string,string> parent=new Dictionary<string,string>();
   internal readonly Dictionary<string,HashSet<string>> Roots=new Dictionary<string,HashSet<string>>();
   internal readonly Dictionary<string,string> Pins=new Dictionary<string,string>();
   internal Islands(IEnumerable<NetworkNode> nodes){foreach(var n in nodes){parent[n.Id]=n.Id;Roots[n.Id]=new HashSet<string>(StringComparer.Ordinal);if(n.Root&&n.Confirmed)Roots[n.Id].Add(n.Network);Pins[n.Id]=n.Gateway?n.Binding??"":"";}}
   internal string Find(string id){var p=parent[id];return p==id?id:parent[id]=Find(p);}
   internal bool Join(string a,string b,bool guarded){
    a=Find(a);b=Find(b);if(a==b)return true;
    var ar=Roots[a];var br=Roots[b];string ap=Pins[a],bp=Pins[b];
    if(guarded){
     if(ap!=""&&bp!=""&&ap!=bp)return false;
     if(ap!=""&&br.Count>0&&!br.Contains(ap)||bp!=""&&ar.Count>0&&!ar.Contains(bp))return false;
     // Normal relays retain their established local behaviour. A gateway edge
     // may never combine two independently rooted local networks.
     if(ar.Count>0&&br.Count>0&&!ar.Overlaps(br))return false;
    }
    if(string.CompareOrdinal(a,b)>0){var t=a;a=b;b=t;}
    parent[b]=a;Roots[a].UnionWith(Roots[b]);
    // Ordinary multi-core components do not acquire a gateway binding of their
    // own. A gateway may remain bound to any original core in that component.
    Pins[a]=ap!=""?ap:bp!=""?bp:guarded?Roots[a].OrderBy(x=>x,StringComparer.Ordinal).FirstOrDefault()??"":"";
    return true;
   }
  }
  static NetworkGraph WithGateways(NetworkNode[] source,double range){
   var nodes=source.Select(n=>new NetworkNode{Id=n.Id,Network=n.Network,Root=n.Root,Gateway=n.Gateway,Tag=n.Tag??"",Binding=n.Binding??"",Confirmed=n.Confirmed&&(!n.Root||!string.IsNullOrEmpty(n.Network)),Position=n.Position,Storage=n.Storage,Supply=n.Supply}).ToArray();
   foreach(var tag in nodes.Where(n=>n.Gateway&&n.Tag!="").GroupBy(n=>n.Tag,StringComparer.Ordinal)){
    var pair=tag.ToArray();if(pair.Length!=2)continue;
    var bound=pair.FirstOrDefault(n=>n.Binding!="");if(bound!=null)foreach(var n in pair)if(n.Binding=="")n.Binding=bound.Binding;
   }
   var spatial=new NetworkGraph(nodes.Select(n=>new NetworkNode{Id=n.Id,Network="",Confirmed=n.Confirmed,Position=n.Position}),range);
   var groups=new Islands(nodes);var local=new Islands(nodes);
   var edges=nodes.ToDictionary(n=>n.Id,n=>new List<string>(),StringComparer.Ordinal);
   Action<string,string> add=(a,b)=>{if(!edges[a].Contains(b)){edges[a].Add(b);edges[b].Add(a);}};
   var byId=nodes.ToDictionary(n=>n.Id,StringComparer.Ordinal);
   var links=nodes.Where(n=>n.Confirmed).SelectMany(n=>spatial.Near(n.Position).Where(o=>string.CompareOrdinal(n.Id,o.Id)<0).Select(o=>(A:n,B:byId[o.Id]))).ToArray();
   foreach(var e in links.Where(e=>!e.A.Gateway&&!e.B.Gateway)){groups.Join(e.A.Id,e.B.Id,false);local.Join(e.A.Id,e.B.Id,false);add(e.A.Id,e.B.Id);}
   foreach(var e in links.Where(e=>e.A.Gateway||e.B.Gateway).OrderBy(e=>e.A.Position.Distance2(e.B.Position)).ThenBy(e=>e.A.Id,StringComparer.Ordinal).ThenBy(e=>e.B.Id,StringComparer.Ordinal))
    if(groups.Join(e.A.Id,e.B.Id,true)){local.Join(e.A.Id,e.B.Id,false);add(e.A.Id,e.B.Id);}
   var states=nodes.Where(n=>n.Gateway).ToDictionary(n=>n.Id,n=>n.Tag==""?"gateway_no_tag":"gateway_no_pair",StringComparer.Ordinal);
   var candidates=new List<NetworkNode[]>();
   // Count all placed gateways, including inaccessible ones. Hiding a third
   // endpoint behind a ward must not make an ambiguous tag usable.
   foreach(var tag in nodes.Where(n=>n.Gateway&&n.Tag!="").GroupBy(n=>n.Tag,StringComparer.Ordinal).OrderBy(g=>g.Key,StringComparer.Ordinal)){
    var pair=tag.ToArray();if(pair.Length!=2){if(pair.Length>2)foreach(var n in pair)states[n.Id]="gateway_ambiguous";continue;}
    if(pair.All(n=>n.Confirmed))candidates.Add(pair);else foreach(var n in pair)states[n.Id]="disconnected";
   }
   var paired=new Dictionary<string,string>(StringComparer.Ordinal);
   bool progress;
   do {
    progress=false;
    foreach(var pair in candidates.ToArray()){
     var a=pair[0];var b=pair[1];string ga=groups.Find(a.Id),gb=groups.Find(b.Id);
     if(groups.Pins[ga]==""&&groups.Pins[gb]=="")continue;
     candidates.Remove(pair);
     if(!groups.Join(a.Id,b.Id,true)){states[a.Id]=states[b.Id]="gateway_conflict";continue;}
     add(a.Id,b.Id);paired[a.Id]=b.Id;paired[b.Id]=a.Id;progress=true;
    }
   }while(progress);
   var anchors=nodes.Where(n=>n.Root&&n.Confirmed).GroupBy(n=>groups.Find(n.Id)).ToDictionary(g=>g.Key,g=>g.OrderBy(n=>n.Network,StringComparer.Ordinal).ThenBy(n=>n.Id,StringComparer.Ordinal).First());
   var identities=anchors.ToDictionary(p=>p.Key,p=>p.Value.Network+"@"+p.Value.Id,StringComparer.Ordinal);
   var bindings=new Dictionary<string,string>();
   foreach(var n in nodes){var g=groups.Find(n.Id);if(n.Gateway)bindings[n.Id]=groups.Pins[g];n.Network=n.Confirmed&&identities.TryGetValue(g,out var identity)?identity:"";}
   var graph=new NetworkGraph(nodes,range,true,edges);
   foreach(var n in nodes){graph.localComponents[n.Id]=local.Find(n.Id);if(!n.Gateway)continue;graph.GatewayBindings[n.Id]=bindings[n.Id];graph.GatewayStates[n.Id]=paired.ContainsKey(n.Id)?graph.Hops.ContainsKey(n.Id)?"connected":"disconnected":states[n.Id];}
   foreach(var pair in paired)graph.GatewayPairs[pair.Key]=pair.Value;
   return graph;
  }
  // Item restrictions apply to the route, not to the entire network. A normal
  // alternate path wins even if a shorter route crosses a gateway.
  public bool CanTransfer(string network,Point source,Point consumer,bool teleportable){
   int route=TransferRoute(network,source,consumer);return route==1||route==2&&teleportable;
  }
  // 0: no route; 1: ordinary local path; 2: requires a gateway. Cached on the
  // immutable topology snapshot, independent of item/stack/count changes.
  public int TransferRoute(string network,Point source,Point consumer){
   if(string.IsNullOrEmpty(network))return 0;
   if(!HasGateways)return Covers(network,source,n=>true)&&Supplies(network,consumer,n=>true)?1:0;
   var key=(network,source,consumer);if(routes.TryGetValue(key,out int cached))return cached;
   if(!supplyComponents.TryGetValue(consumer,out var local)){
    if(supplyComponents.Count>=128)supplyComponents.Clear();
    local=new HashSet<string>(Nodes.Values.Where(n=>Hops.ContainsKey(n.Id)&&n.Position.Distance2(consumer)<=n.Supply*n.Supply).Select(n=>localComponents[n.Id]),StringComparer.Ordinal);supplyComponents[consumer]=local;
   }
   int result=0;bool supplied=false,checkedSupply=false;
   foreach(var n in Nodes.Values)if(n.Network==network&&Hops.ContainsKey(n.Id)&&n.Position.Distance2(source)<=n.Storage*n.Storage){
    if(local.Contains(localComponents[n.Id])){result=1;break;}
    if(!checkedSupply){supplied=Supplies(network,consumer,c=>true);checkedSupply=true;}
    if(supplied)result=2;
   }
   if(routes.Count>=16384)routes.Clear();routes[key]=result;return result;
  }
 }
}
