using System;
using System.Collections.Generic;
using System.Linq;
using RunicStorageNetwork.Logic;

static class GatewayTests {
 static int passed;
 static void Check(bool value,string why){if(!value)throw new Exception("Gateway: "+why);}
 static void Test(string name,Action body){body();passed++;Console.WriteLine("PASS gateway "+name);}
 static NetworkNode N(string id,double x,bool root=false,string identity="A")=>new NetworkNode{Id=id,Position=new Point(x,0,0),Root=root,Network=root?identity:"",Confirmed=true,Storage=20,Supply=20};
 static NetworkNode G(string id,double x,string tag="outpost",string binding=""){var n=N(id,x);n.Gateway=true;n.Tag=tag;n.Binding=binding;return n;}
 static NetworkGraph Graph(params NetworkNode[] nodes)=>NetworkGraph.Automatic(nodes,50);
 static string Net(NetworkGraph g)=>g.Nodes["core"].Network;
 static NetworkNode[] Basic()=>new[]{N("core",0,true),N("relay",50),G("a",100),G("b",5000),N("remote",5050)};
 public static int Run(){
  Test("pair connects any distance and local relay extends coverage",()=>{var g=Graph(Basic());Check(g.GatewayPairs["a"]=="b"&&g.GatewayStates["b"]=="connected"&&g.Choose(new Point(5065,0,0),n=>true)==Net(g),"missing bridge");Check(g.Choose(new Point(5080,0,0),n=>true)==null,"infinite radius");});
  Test("teleportable crosses; local nonteleportable remains available",()=>{var g=Graph(Basic());var at=new Point(5050,0,0);Check(g.CanTransfer(Net(g),new Point(0,0,0),at,true),"wood blocked");Check(!g.CanTransfer(Net(g),new Point(0,0,0),at,false),"remote iron");Check(g.CanTransfer(Net(g),new Point(5055,0,0),at,false),"local iron blocked");});
  Test("same source gives different counts for opposite consumers",()=>{var g=Graph(Basic());Check(g.TransferRoute(Net(g),new Point(0,0,0),new Point(0,0,0))==1&&g.TransferRoute(Net(g),new Point(0,0,0),new Point(5000,0,0))==2,"route cache leaks between sides");});
  Test("empty tags never pair",()=>{var g=Graph(N("core",0,true),G("a",50,""),G("b",5000,""));Check(g.GatewayPairs.Count==0&&!g.Hops.ContainsKey("b"),"empty pair");});
  Test("three same-tag gateways all fail closed including an inaccessible third",()=>{var nodes=Basic().Concat(new[]{G("c",8000)}).ToArray();nodes.Last().Confirmed=false;var g=Graph(nodes);Check(g.GatewayPairs.Count==0&&g.GatewayStates["a"]=="gateway_ambiguous"&&!g.Hops.ContainsKey("remote"),"ambiguous pairing");});
  Test("two independent rooted networks cannot merge through a tag",()=>{var g=Graph(Basic().Concat(new[]{N("foreign",5050,true,"B")}).ToArray());Check(g.GatewayPairs.Count==0&&g.Nodes["foreign"].Network!=Net(g),"networks merged");});
  Test("bound pair ignores a foreign nearby core",()=>{var nodes=Basic();nodes[2].Binding="A";var g=Graph(nodes.Concat(new[]{N("foreign",5035,true,"B")}).ToArray());Check(g.GatewayPairs.Count==2&&g.Nodes["b"].Network==Net(g)&&g.Nodes["foreign"].Network!=Net(g),"rebound remote gateway");Check(g.GatewayBindings["b"]=="A","pair did not inherit identity");});
  Test("mismatched persisted bindings refuse pairing",()=>{var nodes=Basic();nodes[2].Binding="A";nodes[3].Binding="B";var g=Graph(nodes);Check(g.GatewayPairs.Count==0&&g.GatewayStates["a"]=="gateway_conflict","binding overwritten");});
  Test("normal alternate path removes restriction even if gateway is shorter",()=>{var nodes=new List<NetworkNode>{N("core",0,true),G("a",50),G("b",200)};for(int x=100;x<200;x+=50)nodes.Add(N("r"+x,x));var g=Graph(nodes.ToArray());Check(g.GatewayPairs.Count==2&&g.CanTransfer(Net(g),new Point(0,0,0),new Point(200,0,0),false),"shortest path incorrectly forbids iron");});
  Test("removing ordinary alternative invalidates cached local route",()=>{var nodes=new[]{N("core",0,true),G("a",50),N("r",100),G("b",150)};var first=Graph(nodes);Check(first.CanTransfer(Net(first),new Point(0,0,0),new Point(150,0,0),false),"initial path");var next=Graph(nodes.Where(n=>n.Id!="r").ToArray());Check(!next.CanTransfer(Net(next),new Point(0,0,0),new Point(150,0,0),false),"stale cache after removal");});
  Test("removing pair or relay disconnects remote side",()=>{foreach(string id in new[]{"a","b","relay","core"}){var g=Graph(Basic().Where(n=>n.Id!=id).ToArray());Check(!g.Hops.ContainsKey("remote"),"remaining path after removing "+id);}});
  Test("tag change disconnects then reconnects",()=>{var nodes=Basic();nodes[3].Tag="renamed";Check(!Graph(nodes).Hops.ContainsKey("remote"),"old tag cached");nodes[2].Tag="renamed";Check(Graph(nodes).Hops.ContainsKey("remote"),"new tag not paired");});
  Test("multiple pairs including chained bridges",()=>{var g=Graph(Basic().Concat(new[]{G("c",5090,"next"),G("d",9000,"next"),N("last",9040)}).ToArray());Check(g.GatewayPairs.Count==4&&g.Hops.ContainsKey("last")&&g.GatewayBindings["d"]=="A","chained pairing");Check(!g.CanTransfer(Net(g),new Point(0,0,0),new Point(9040,0,0),false),"chained restriction");});
  Test("pair without a core stays inactive",()=>{var g=Graph(G("a",0,binding:"lost"),G("b",5000,binding:"lost"));Check(g.Hops.Count==0&&g.GatewayStates["a"]=="disconnected","self-powered bridge");});
  Test("persisted root identity survives new world object IDs",()=>{var g=Graph(N("new-core-id",0,true,"saved-guid"),G("new-a",50,binding:"saved-guid"),G("new-b",5000,binding:"saved-guid"));Check(g.Hops.ContainsKey("new-b")&&g.GatewayBindings["new-b"]=="saved-guid","runtime ID used as persistent binding");});
  Test("deterministic under reversed discovery order",()=>{var nodes=Basic();var a=Graph(nodes);var b=Graph(nodes.Reverse().ToArray());Check(a.Nodes.All(p=>b.Nodes[p.Key].Network==p.Value.Network)&&a.GatewayBindings.All(p=>b.GatewayBindings[p.Key]==p.Value),"unstable pairing");});
  Test("cycles count each chest once",()=>{var g=Graph(N("core",0,true),G("a",30,"one"),G("b",5000,"one"),G("c",5020,"two"),G("d",20,"two"));var pool=g.Pool(Net(g),new[]{new KeyValuePair<string,Point>("chest",new Point(5010,0,0))},n=>true).ToArray();Check(pool.Length==1&&g.Hops.Count==5,"cycle duplicated sources");});
  Test("10000 item lookups reuse one cached route",()=>{var g=Graph(Basic());for(int i=0;i<10000;i++)Check(g.TransferRoute(Net(g),new Point(0,0,0),new Point(5000,0,0))==2,"cached result");var field=typeof(NetworkGraph).GetField("routes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);Check(((System.Collections.IDictionary)field.GetValue(g)).Count==1,"per-item route expansion");});
  Test("old networks preserve automatic local joining",()=>{var g=Graph(N("core",0,true),N("other",40,true,"B"),N("relay",80));Check(!g.HasGateways&&g.Hops.ContainsKey("relay")&&g.Nodes["other"].Network==Net(g),"legacy behaviour changed");});
  Test("unconfirmed endpoint cannot form bridge",()=>{var nodes=Basic();nodes[3].Confirmed=false;Check(Graph(nodes).GatewayPairs.Count==0,"inaccessible gateway accepted");});
  Test("gateway keeps binding when its original core joins another local core",()=>{var nodes=new[]{N("core",0,true,"A"),N("other",40,true,"B"),N("r",80),G("a",100,binding:"B"),G("b",5000,binding:"B")};var first=Graph(nodes);var reversed=Graph(nodes.Reverse().ToArray());Check(first.GatewayPairs.Count==2&&first.Hops.ContainsKey("b")&&first.GatewayBindings["a"]=="B","ordinary joined core invalidated binding");Check(first.Nodes.All(p=>reversed.Nodes[p.Key].Network==p.Value.Network)&&reversed.GatewayBindings["a"]=="B","binding depends on discovery order");});
  return passed;
 }
}
