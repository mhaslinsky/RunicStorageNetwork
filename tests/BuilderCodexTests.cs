using System;
using System.Linq;
using RunicStorageNetwork.Logic;

static class BuilderCodexTests {
 static int passed;
 static NetworkNode Node(string id,double x,bool root=false,string identity="A")=>new NetworkNode{Id=id,Position=new Point(x,0,0),Root=root,Network=root?identity:"",Confirmed=true,Storage=20,Supply=20};
 static NetworkNode Gate(string id,double x){var n=Node(id,x);n.Gateway=true;n.Tag="bridge";return n;}
 static NetworkGraph Graph(params NetworkNode[] nodes)=>NetworkGraph.Automatic(nodes,50);
 static bool Access(NetworkGraph g,string binding,double x){string net=g.BoundNetwork(binding);return net!=null&&g.Supplies(net,new Point(x,0,0),n=>true,true);}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Test(string name,Action body){body();passed++;Console.WriteLine("PASS builder codex "+name);}
 public static int Run(){
  Test("core link includes 50 metres and excludes beyond it",()=>{var g=Graph(Node("core",0,true));Check(Access(g,"A",50)&&!Access(g,"A",50.001),"core boundary");});
  Test("connected relay extends wearable range but not ordinary supply",()=>{var g=Graph(Node("core",0,true),Node("relay",50));Check(Access(g,"A",100)&&!Access(g,"A",100.001)&&g.Choose(new Point(90,0,0),n=>true)==null,"relay boundary or ordinary coverage changed");});
  Test("configured link range replaces default fifty metres",()=>{var g=NetworkGraph.Automatic(new[]{Node("core",0,true)},30);Check(Access(g,"A",30)&&!Access(g,"A",30.001),"hardcoded fifty metre range");});
  Test("three-dimensional distance counts height",()=>{var g=Graph(Node("core",0,true));Check(g.Supplies(g.BoundNetwork("A"),new Point(30,40,0),n=>true,true)&&!g.Supplies(g.BoundNetwork("A"),new Point(30,40.01,0),n=>true,true),"cylindrical coverage");});
  Test("nearby foreign core cannot supply a bound book",()=>{var g=Graph(Node("core",0,true),Node("foreign",80,true,"B"));Check(Access(g,"A",45)&&!Access(g,"B",0)&&!Access(g,"A",70),"foreign network fallback");});
  Test("empty or missing binding never chooses the nearest network",()=>{var g=Graph(Node("core",0,true));Check(!Access(g,"",0)&&!Access(g,"missing",0)&&!Access(g,null,0),"unbound automatic assignment");});
  Test("reloaded runtime IDs preserve persistent binding",()=>{var g=Graph(Node("new-world-id",0,true,"saved-guid"));Check(Access(g,"saved-guid",40)&&!Access(g,"old-world-id",40),"binding stored runtime ID");});
  Test("rootless disconnected relay cannot supply wearable",()=>{var g=Graph(Node("core",0,true),Node("orphan",120));Check(!Access(g,"A",140),"orphan became root");});
  Test("inaccessible root refuses access even through nearby relay",()=>{var root=Node("core",0,true);root.Confirmed=false;var g=Graph(root,Node("relay",40));Check(!Access(g,"A",70),"inaccessible root");});
  Test("ward-denied relay cannot extend range",()=>{var relay=Node("relay",50);relay.Confirmed=false;Check(!Access(Graph(Node("core",0,true),relay),"A",90),"inaccessible relay");});
  Test("merged roots preserve book binding after canonical anchor changes",()=>{var g=Graph(Node("a",0,true,"A"),Node("b",40,true,"B"),Node("r",80));Check(g.BoundNetwork("A")==g.BoundNetwork("B")&&Access(g,"B",125),"noncanonical root binding lost");var split=Graph(Node("b-new",40,true,"B"),Node("r-new",80));Check(Access(split,"B",125)&&!Access(split,"A",125),"removed anchor used");});
  Test("removing the bound core does not rebind to a foreign replacement",()=>{var g=Graph(Node("replacement",0,true,"B"));Check(!Access(g,"A",10)&&Access(g,"B",10),"replacement inherited book");});
  Test("wearable access does not enlarge chest collection range",()=>{var g=Graph(Node("core",0,true));Check(Access(g,"A",45)&&!g.Covers(g.BoundNetwork("A"),new Point(45,0,0),n=>true),"player acted as storage relay");});
  Test("gateway by itself is not a wearable relay",()=>{var g=Graph(Node("core",0,true),Gate("a",40),Gate("b",5000));Check(!Access(g,"A",5040),"gateway accepted without relay/core");});
  Test("remote relay permits portable building across gateway",()=>{var g=Graph(Node("core",0,true),Gate("a",40),Gate("b",5000),Node("relay",5040));Check(Access(g,"A",5090),"remote linked radius");Check(g.CanTransfer(g.BoundNetwork("A"),new Point(0,0,0),new Point(5090,0,0),true,true)&&!g.CanTransfer(g.BoundNetwork("A"),new Point(0,0,0),new Point(5090,0,0),false,true),"gateway item restriction bypassed");});
  Test("local metals available through wearable while remote metals remain blocked",()=>{var g=Graph(Node("core",0,true),Gate("a",40),Gate("b",5000),Node("relay",5040));Check(g.CanTransfer(g.BoundNetwork("A"),new Point(5040,0,0),new Point(5090,0,0),false,true),"local metal blocked");});
  Test("wearable route cache does not grant normal terminal or crafting range",()=>{var g=Graph(Node("core",0,true),Gate("a",40),Gate("b",5000),Node("relay",5040));var source=new Point(0,0,0);var at=new Point(5090,0,0);Check(g.TransferRoute(g.BoundNetwork("A"),source,at,true)==2&&g.TransferRoute(g.BoundNetwork("A"),source,at)==0&&g.TransferRoute(g.BoundNetwork("A"),source,at,true)==2,"consumer mode missing from cache key");});
  Test("normal alternate path is preferred for metals",()=>{var g=Graph(Node("core",0,true),Gate("a",40),Gate("b",160),Node("r1",80),Node("r2",120),Node("r3",200));Check(g.CanTransfer(g.BoundNetwork("A"),new Point(0,0,0),new Point(250,0,0),false,true),"ordinary path discarded");});
  Test("player lookups never add moving graph nodes",()=>{var g=Graph(Node("core",0,true));int nodes=g.Nodes.Count;for(int i=0;i<1000;i++)Access(g,"A",i%70);Check(g.Nodes.Count==nodes&&g.Hops.Count==1,"portable player became topology node");});
  return passed;
 }
}
