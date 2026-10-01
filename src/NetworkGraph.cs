using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicStorageNetwork.Logic {
 public struct Point {
  public double X,Y,Z;
  public Point(double x,double y,double z){X=x;Y=y;Z=z;}
  public double Distance2(Point p){double x=X-p.X,y=Y-p.Y,z=Z-p.Z;return x*x+y*y+z*z;}
 }
 public sealed class NetworkNode {
  public string Id,Network;
  public bool Gateway;
  public string Tag="",Binding="";
  public Point Position;
  public bool Root,Confirmed,ChoiceRequired;
  public double Storage,Supply;
 }
 // Immutable per-revision view. Only confirmed, placed nodes are fed to the graph.
 public sealed partial class NetworkGraph {
  public readonly Dictionary<string,NetworkNode> Nodes;
  public readonly Dictionary<string,List<string>> Neighbors=new Dictionary<string,List<string>>(StringComparer.Ordinal);
  public readonly Dictionary<string,int> Hops=new Dictionary<string,int>(StringComparer.Ordinal);
  public readonly Dictionary<string,string> Parent=new Dictionary<string,string>(StringComparer.Ordinal);
  public readonly Dictionary<string,string> Roots=new Dictionary<string,string>(StringComparer.Ordinal);
  readonly double range;
  readonly Dictionary<(int,int,int),List<NetworkNode>> cells=new Dictionary<(int,int,int),List<NetworkNode>>();
  (int,int,int) Cell(Point p)=>((int)Math.Floor(p.X/range),(int)Math.Floor(p.Y/range),(int)Math.Floor(p.Z/range));
  public NetworkGraph(IEnumerable<NetworkNode> input,double linkRange,bool multipleRoots=false,Dictionary<string,List<string>> edges=null){
   if(linkRange<=0||double.IsNaN(linkRange))throw new ArgumentOutOfRangeException(nameof(linkRange));range=linkRange;
   Nodes=input.GroupBy(n=>n.Id,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Single(),StringComparer.Ordinal);
   foreach(var n in Nodes.Values.Where(n=>n.Confirmed).OrderBy(n=>n.Id,StringComparer.Ordinal)){
    var key=Cell(n.Position);if(!cells.TryGetValue(key,out var list))cells[key]=list=new List<NetworkNode>();list.Add(n);
   }
   foreach(var n in Nodes.Values){Neighbors[n.Id]=edges!=null?edges[n.Id]:Near(n.Position).Where(o=>o.Id!=n.Id&&n.Confirmed&&n.Network!=""&&o.Network==n.Network).Select(o=>o.Id).OrderBy(id=>id,StringComparer.Ordinal).ToList();}
   foreach(var g in Nodes.Values.Where(n=>n.Root&&n.Confirmed&&!string.IsNullOrEmpty(n.Network)).GroupBy(n=>n.Network,StringComparer.Ordinal)){
    // Conflicting roots are not a valid network, even if supplied by corrupt persisted data.
    if(!multipleRoots&&g.Count()!=1)continue;var root=g.OrderBy(n=>n.Id,StringComparer.Ordinal).First();Roots[root.Network]=root.Id;var queue=new Queue<string>();foreach(var source in g){Hops[source.Id]=0;queue.Enqueue(source.Id);}
    while(queue.Count>0){string id=queue.Dequeue();foreach(string next in Neighbors[id])if(!Hops.ContainsKey(next)){Hops[next]=Hops[id]+1;Parent[next]=id;queue.Enqueue(next);}}
   }
  }
  public static NetworkGraph Automatic(IEnumerable<NetworkNode> input,double linkRange){
   var source=input.ToArray();if(source.Any(n=>n.Gateway))return WithGateways(source,linkRange);
   var nodes=source.Select(n=>new NetworkNode{Id=n.Id,Network=n.Network,Root=n.Root,Confirmed=n.Confirmed&&(!n.Root||!string.IsNullOrEmpty(n.Network)),Position=n.Position,Storage=n.Storage,Supply=n.Supply}).ToArray();
   // Use spatial buckets for adjacency even on unbound relays. Components are
   // derived, not persisted: removing a bridge splits them automatically.
   var spatial=new NetworkGraph(nodes.Select(n=>new NetworkNode{Id=n.Id,Network="",Confirmed=n.Confirmed,Position=n.Position}),linkRange);
   var seen=new HashSet<string>();var byId=nodes.ToDictionary(n=>n.Id,StringComparer.Ordinal);
   foreach(var seed in nodes.Where(n=>n.Confirmed).OrderBy(n=>n.Id,StringComparer.Ordinal)){
    if(!seen.Add(seed.Id))continue;var component=new List<NetworkNode>();var queue=new Queue<NetworkNode>();queue.Enqueue(seed);
    while(queue.Count>0){var n=queue.Dequeue();component.Add(n);foreach(var neighbor in spatial.Near(n.Position))if(seen.Add(neighbor.Id))queue.Enqueue(byId[neighbor.Id]);}
    var anchor=component.Where(n=>n.Root).OrderBy(n=>n.Network,StringComparer.Ordinal).ThenBy(n=>n.Id,StringComparer.Ordinal).FirstOrDefault();
    string network=anchor==null?"":anchor.Network+"@"+anchor.Id;
    foreach(var n in component)n.Network=network;
   }
   foreach(var n in nodes.Where(n=>!n.Confirmed))n.Network="";
   return new NetworkGraph(nodes,linkRange,true);
  }
  public IEnumerable<NetworkNode> Near(Point point){
   var c=Cell(point);for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)
    if(cells.TryGetValue((c.Item1+x,c.Item2+y,c.Item3+z),out var list))foreach(var n in list)if(n.Position.Distance2(point)<=range*range)yield return n;
  }
  public Dictionary<string,string> Candidates(Point point,Func<NetworkNode,bool> allowed){
   return Near(point).Where(n=>Hops.ContainsKey(n.Id)&&allowed(n)).OrderBy(n=>n.Position.Distance2(point)).ThenBy(n=>n.Id,StringComparer.Ordinal)
    .GroupBy(n=>n.Network,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.First().Id,StringComparer.Ordinal);
  }
  // Placement needs every direct connection, including alternate links in one network.
  public NetworkNode[] PlacementConnections(Point point,Func<NetworkNode,bool> allowed)=>Near(point)
   .Where(n=>Hops.ContainsKey(n.Id)&&allowed(n)).OrderBy(n=>n.Position.Distance2(point))
   .ThenBy(n=>n.Id,StringComparer.Ordinal).ToArray();
  public string Choose(Point point,Func<NetworkNode,bool> allowed){
   return Nodes.Values.Where(n=>Hops.ContainsKey(n.Id)&&n.Position.Distance2(point)<=n.Supply*n.Supply&&allowed(n))
    .OrderBy(n=>n.Position.Distance2(point)).ThenBy(n=>n.Network,StringComparer.Ordinal).ThenBy(n=>n.Id,StringComparer.Ordinal).Select(n=>n.Network).FirstOrDefault();
  }
  public bool Covers(string network,Point point,Func<NetworkNode,bool> allowed)=>Nodes.Values.Any(n=>n.Network==network&&Hops.ContainsKey(n.Id)&&n.Position.Distance2(point)<=n.Storage*n.Storage&&allowed(n));
  public bool Supplies(string network,Point point,Func<NetworkNode,bool> allowed)=>Nodes.Values.Any(n=>n.Network==network&&Hops.ContainsKey(n.Id)&&n.Position.Distance2(point)<=n.Supply*n.Supply&&allowed(n));
  public IEnumerable<string> Pool(string network,IEnumerable<KeyValuePair<string,Point>> sources,Func<NetworkNode,bool> allowed)=>sources.Where(s=>Covers(network,s.Value,allowed)).Select(s=>s.Key).Distinct(StringComparer.Ordinal).OrderBy(s=>s,StringComparer.Ordinal);
  public static string AutoBinding(string saved,bool choiceRequired,bool complete,IEnumerable<string> candidates,out bool requireChoice){
   requireChoice=choiceRequired;if(!string.IsNullOrEmpty(saved)||choiceRequired||!complete)return saved;
   var ids=candidates.Distinct(StringComparer.Ordinal).ToArray();if(ids.Length>1)requireChoice=true;return ids.Length==1?ids[0]:saved;
  }
  public static string RootIdentity(string nodeId)=>"core:"+nodeId;
  // ZDO.Load assigns a fresh runtime ID. Never derive a saved network's
  // identity again on load; relay bindings refer to the persisted string.
  public static string PersistentIdentity(string saved)=>string.IsNullOrEmpty(saved)?"core:"+Guid.NewGuid().ToString("N"):saved;
  public static bool MatchesRoot(string saved,string requested)=>!string.IsNullOrEmpty(saved)&&string.Equals(saved,requested,StringComparison.Ordinal);
 }
}
