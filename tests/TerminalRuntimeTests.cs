#if TERMINAL_RUNTIME_TESTS
#pragma warning disable 0649
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunicStorageNetwork;
using RunicStorageNetwork.Logic;
namespace UnityEngine {
 public class Object {public static implicit operator bool(Object o)=>o!=null;}
 public struct Vector3 {public float x;public static float Distance(Vector3 a,Vector3 b)=>Math.Abs(a.x-b.x);}
 public class Transform {public Vector3 position;}
 public class GameObject:Object {public string name;internal ItemDrop Drop;internal StorageCodex Codex;public T GetComponent<T>() where T:class=>(Drop as T)??(Codex as T);}
 public static class Time {public static float unscaledTime;}
}
struct Vector2i {public int x,y;public Vector2i(int x,int y){this.x=x;this.y=y;}}
struct ZDOID {public int Id;public static ZDOID None=>default;public static bool operator ==(ZDOID a,ZDOID b)=>a.Id==b.Id;public static bool operator !=(ZDOID a,ZDOID b)=>!(a==b);public override bool Equals(object o)=>o is ZDOID other&&other==this;public override int GetHashCode()=>Id;}
static class ZDOVars {public static int s_creator=1;}
class ZDO {internal UnityEngine.GameObject Prefab;internal UnityEngine.Vector3 Position;internal long Creator=1;internal bool Valid=true;public UnityEngine.Vector3 GetPosition()=>Position;public long GetLong(int key,long fallback)=>Creator;}
class ZPackage {
 readonly MemoryStream stream;readonly BinaryWriter writer;readonly BinaryReader reader;bool reading;
 public ZPackage():this(new byte[0]){}public ZPackage(byte[] data){stream=new MemoryStream();stream.Write(data,0,data.Length);writer=new BinaryWriter(stream);reader=new BinaryReader(stream);}
 void Read(){if(!reading){stream.Position=0;reading=true;}}
 public void Write(string x)=>writer.Write(x);public void Write(int x)=>writer.Write(x);public void Write(bool x)=>writer.Write(x);public void Write(byte[] x){writer.Write(x.Length);writer.Write(x);}
 public string ReadString(){Read();return reader.ReadString();}public int ReadInt(){Read();return reader.ReadInt32();}public bool ReadBool(){Read();return reader.ReadBoolean();}public byte[] ReadByteArray(){int n=ReadInt();return reader.ReadBytes(n);}public byte[] GetArray()=>stream.ToArray();
}
class ItemDrop:UnityEngine.Object {
 public ItemData m_itemData;
 public class Shared {public int m_maxStackSize=50;}
 public class ItemData {
  public UnityEngine.GameObject m_dropPrefab;public int m_stack,m_quality=1;public bool m_equipped;public Vector2i m_gridPos;public Shared m_shared=new Shared();public string Custom="";
  public ItemData Clone()=>(ItemData)MemberwiseClone();
  public void Save(ZPackage p){p.Write(m_dropPrefab.name);p.Write(m_stack);p.Write(m_quality);p.Write(m_gridPos.x);p.Write(m_gridPos.y);p.Write(Custom);p.Write(m_equipped);}
  public static ItemData Load(ZPackage p)=>new ItemData{m_dropPrefab=ZNetScene.instance.GetPrefab(p.ReadString()),m_stack=p.ReadInt(),m_quality=p.ReadInt(),m_gridPos=new Vector2i(p.ReadInt(),p.ReadInt()),Custom=p.ReadString(),m_equipped=p.ReadBool()};
 }
}
class Inventory {
 public int Width=2,Height=1,Adds,FailAt;public bool ThrowAfter,FailRestore;readonly List<ItemDrop.ItemData> items=new List<ItemDrop.ItemData>();
 public Inventory(string name,object background,int w,int h){Width=w;Height=h;}
 public int GetWidth()=>Width;public int GetHeight()=>Height;public List<ItemDrop.ItemData> GetAllItems()=>items;public bool ContainsItem(ItemDrop.ItemData item)=>items.Contains(item);public ItemDrop.ItemData GetItemAt(int x,int y)=>items.FirstOrDefault(i=>i.m_gridPos.x==x&&i.m_gridPos.y==y);
 public bool Add(ItemDrop.ItemData item,int amount,int x,int y){Adds++;if(Adds==FailAt&&!ThrowAfter)return false;var at=GetItemAt(x,y);if(at!=null)at.m_stack+=amount;else{at=item.Clone();at.m_stack=amount;at.m_gridPos=new Vector2i(x,y);items.Add(at);}if(Adds==FailAt&&ThrowAfter)throw new Exception("other mod callback");return true;}
 public bool RemoveItem(ItemDrop.ItemData item,int amount){if(FailRestore)throw new Exception("rollback blocked");item.m_stack-=amount;if(item.m_stack==0)items.Remove(item);return true;}
 public void Save(ZPackage p){p.Write(items.Count);foreach(var item in items)item.Save(p);}public void Load(ZPackage p){items.Clear();int count=p.ReadInt();for(int i=0;i<count;i++)items.Add(ItemDrop.ItemData.Load(p));}
}
class Player:UnityEngine.Object {public static Player m_localPlayer;public Inventory Inventory=new Inventory("player",null,2,1);public UnityEngine.Transform transform=new UnityEngine.Transform();public bool Dead;public Inventory GetInventory()=>Inventory;public bool IsDead()=>Dead;public ZDOID GetZDOID()=>new ZDOID{Id=1};public long GetPlayerID()=>1;}
class ZNet {public static long GetUID()=>3;}
class ZNetScene:UnityEngine.Object {public static ZNetScene instance=new ZNetScene();public Dictionary<string,UnityEngine.GameObject> Prefabs=new Dictionary<string,UnityEngine.GameObject>();public UnityEngine.GameObject GetPrefab(string name)=>Prefabs.TryGetValue(name,out var p)?p:null;}
namespace RunicStorageNetwork {
 static class GatewayRuntime {internal static bool ValidatePlan(Operation op,IEnumerable<Debit> plan)=>true;}
 static class Plugin {internal static bool Enabled=true;internal static void Debug(string t){}internal static void Error(string t,Exception e){}internal static void Critical(string id,string reason){}}
 class Core:UnityEngine.Object {internal bool Valid=true;internal ZDOID Id=new ZDOID{Id=2};internal UnityEngine.Transform transform=new UnityEngine.Transform();internal T GetComponent<T>() where T:class=>new NetworkMember() as T;}
 class StorageCodex:UnityEngine.Object {internal bool Valid=true;internal ZDOID Id=new ZDOID{Id=4};internal UnityEngine.Transform transform=new UnityEngine.Transform();internal static StorageCodex Live;internal static StorageCodex Find(ZDOID id)=>Live!=null&&Live.Valid&&Live.Id==id?Live:null;}
 static class Topology {internal static bool Connected=true;internal static bool Supplies(Core core,UnityEngine.Vector3 point,long player)=>Connected;}
 class NetworkMember {internal string SavedNetwork="network";}
 static class Access {internal static bool Allowed=true;internal static float? DeniedAt;internal static bool Ward(UnityEngine.Vector3 point,long player)=>Allowed&&point.x!=DeniedAt;}
 class Operation {
  internal string Id,Target,Network;internal bool Build,Quote;internal int Quality,Multiplier;internal ZDOID Actor,Station,Core;internal long Peer,PlayerId;internal List<Need> Needs;internal List<Stock> PlayerStock=new List<Stock>();internal string[] Sources;
  internal bool Withdrawal=>Target.StartsWith(TerminalTransfer.Prefix);internal static RunicStorageNetwork.Core Root;
  internal static RunicStorageNetwork.Core CoreObject(ZDOID id)=>Root;
  internal bool Validate(out Player p,out RunicStorageNetwork.Core c,out string why)=>TerminalTransfer.Validate(this,out p,out c,out why);
 }
 partial class RemoteContext {
  readonly Operation op;internal static readonly Dictionary<ZDOID,ZDO> Records=new Dictionary<ZDOID,ZDO>();
  internal RemoteContext(Operation value){op=value;}
  internal static ZDO Data(ZDOID id)=>Records.TryGetValue(id,out var z)&&z.Valid?z:null;
  internal static UnityEngine.GameObject Prefab(ZDO z)=>z?.Prefab;
  internal bool Ward(UnityEngine.Vector3 point)=>Access.Ward(point,op.PlayerId);
  internal bool Validate(out string why){why="terminal unavailable";return TerminalTransfer.Requirements(op,out why)&&TerminalPoint(Player.m_localPlayer.transform.position,out _);}
 }
 static class R {internal static object Call(Inventory inv,string method,Type[] types,params object[] args)=>inv.Add((ItemDrop.ItemData)args[0],(int)args[1],(int)args[2],(int)args[3]);}
 static class Actions {internal class Pending {internal Operation Op;internal Player Player;}internal static Pending Waiting;internal static bool Propose(Pending p,List<Debit> d){p.Op.Sources=d.Select(x=>x.Source).Distinct().ToArray();return true;}}
 static class CraftPreparation {internal static bool HasReservation;}
 static class NetworkTerminal {internal static bool Visible=true;internal static StorageCodex ShownAt;internal static bool Showing(StorageCodex access,Core core)=>Visible&&ShownAt==access;internal static void TransferStatus(string key,params object[] args){}}
 static class StorageIndex {internal static List<Stock> Counts=new List<Stock>();internal static List<Stock> Query(Core c,long player,IEnumerable<Need> needs,UnityEngine.Vector3? point=null)=>Counts;internal static void Reconcile(Core c){}}
 class InventoryDelta {internal class Part {internal ItemDrop.ItemData Item;internal int Amount;}internal List<Part> Parts=new List<Part>();}
 static class Wire {internal static void Debits(ZPackage p,List<Debit> ds){p.Write(ds.Count);foreach(var d in ds){p.Write(d.Source);p.Write(d.Item);p.Write(d.Quality);p.Write(d.Amount);}}internal static List<Debit> Debits(ZPackage p){int count=p.ReadInt();var ds=new List<Debit>();for(int i=0;i<count;i++)ds.Add(new Debit(p.ReadString(),p.ReadString(),p.ReadInt(),p.ReadInt()));return ds;}}
 class Transport {
  internal static Transport Instance=new Transport();internal static long Server=7;internal static int InternalMutation;internal List<Operation> Requests=new List<Operation>();internal List<(string Id,bool Success)> Results=new List<(string,bool)>();
  internal void Begin(Operation op)=>Requests.Add(op);internal void Result(string id,bool success,string reason)=>Results.Add((id,success));
 }
}
static class TerminalRuntimeTests {
 static int passed;static Core core;static Player player;static StorageCodex access;
 static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 static ItemDrop.ItemData Item(int amount,string custom="",int x=0)=>new ItemDrop.ItemData{m_dropPrefab=ZNetScene.instance.GetPrefab("Wood"),m_stack=amount,Custom=custom,m_gridPos=new Vector2i(x,0)};
 static void Test(string name,Action body){
  TerminalTransfer.Clear();UnityEngine.Time.unscaledTime=0;Plugin.Enabled=true;Access.Allowed=true;core=Operation.Root=new Core();player=Player.m_localPlayer=new Player();Transport.Instance=new Transport();Actions.Waiting=null;CraftPreparation.HasReservation=false;NetworkTerminal.Visible=true;
  access=StorageCodex.Live=new StorageCodex();NetworkTerminal.ShownAt=access;Topology.Connected=true;Access.DeniedAt=null;
  RemoteContext.Records.Clear();RemoteContext.Records[access.Id]=new ZDO{Prefab=new UnityEngine.GameObject{Codex=access},Position=access.transform.position};
  var go=new UnityEngine.GameObject{name="Wood"};go.Drop=new ItemDrop{m_itemData=new ItemDrop.ItemData{m_dropPrefab=go}};ZNetScene.instance.Prefabs["Wood"]=go;StorageIndex.Counts=new List<Stock>{new Stock("chest","Wood",1,100)};
  body();passed++;Console.WriteLine("PASS terminal runtime "+name);
 }
 static string Start(int amount=10){Check(TerminalTransfer.Start(access,core,player,"Wood",1,amount),"start failed");return Transport.Instance.Requests.Last().Id;}
 static ZPackage Reply(string id,int amount=10,string custom="",int quality=1){
  var p=new ZPackage();p.Write(id);Wire.Debits(p,new List<Debit>{new Debit("chest","Wood",quality,amount)});p.Write(1);p.Write("chest");var item=Item(amount,custom);item.m_quality=quality;
  p.Write(TerminalTransfer.Pack(new InventoryDelta{Parts=new List<InventoryDelta.Part>{new InventoryDelta.Part{Item=item,Amount=amount}}}));return p;
 }
 public static int Main(){try{
  Test("double click sends only one transfer",()=>{Start();Check(!TerminalTransfer.Start(access,core,player,"Wood",1,10)&&Transport.Instance.Requests.Count==1,"duplicate accepted");});
  Test("owner parcel preserves custom data and delivers exact amount",()=>{string id=Start();TerminalTransfer.Ready(7,Reply(id,custom:"gem-data"));Check(player.Inventory.GetAllItems().Single().Custom=="gem-data"&&player.Inventory.GetAllItems().Single().m_stack==10,"metadata/count lost");});
  Test("duplicate ready repeats receipt without giving items again",()=>{string id=Start();TerminalTransfer.Ready(7,Reply(id));TerminalTransfer.Ready(7,Reply(id));Check(player.Inventory.GetAllItems().Sum(i=>i.m_stack)==10&&Transport.Instance.Results.Count==2,"duplicate delivery");});
  Test("non-coordinator cannot deliver",()=>{string id=Start();TerminalTransfer.Ready(8,Reply(id));Check(player.Inventory.GetAllItems().Count==0&&Transport.Instance.Results.Count==0,"wrong sender");});
  Test("closing window refuses paid parcel for owner rollback",()=>{string id=Start();NetworkTerminal.Visible=false;TerminalTransfer.Cancel();TerminalTransfer.Ready(7,Reply(id));Check(player.Inventory.GetAllItems().Count==0&&!Transport.Instance.Results.Last().Success,"closed transfer delivered");Check(TerminalTransfer.Busy,"unconfirmed rollback forgotten");TerminalTransfer.Refused(id,"action refused");Check(!TerminalTransfer.Busy,"confirmed rollback stuck");});
  Test("walking away invalidates delivery",()=>{string id=Start();player.transform.position=new UnityEngine.Vector3{x=20};TerminalTransfer.Ready(7,Reply(id));Check(player.Inventory.GetAllItems().Count==0&&!Transport.Instance.Results.Last().Success,"out of reach delivery");});
  Test("death invalidates delivery",()=>{string id=Start();player.Dead=true;TerminalTransfer.Ready(7,Reply(id));Check(player.Inventory.GetAllItems().Count==0,"dead actor delivery");});
  Test("changed access invalidates delivery",()=>{string id=Start();Access.Allowed=false;TerminalTransfer.Ready(7,Reply(id));Check(player.Inventory.GetAllItems().Count==0,"access bypass");});
  Test("unexpected quality is not delivered",()=>{string id=Start();TerminalTransfer.Ready(7,Reply(id,quality:2));Check(player.Inventory.GetAllItems().Count==0&&!Transport.Instance.Results.Last().Success,"wrong quality");});
  Test("full inventory does not consume or overwrite carried items",()=>{player.Inventory.Width=1;var existing=Item(50);player.Inventory.GetAllItems().Add(existing);string id=Start();TerminalTransfer.Ready(7,Reply(id));Check(player.Inventory.GetAllItems().Single()==existing&&existing.m_stack==50&&!Transport.Instance.Results.Last().Success,"full inventory mutated");});
  Test("matching partial stack works without free slots",()=>{player.Inventory.Width=1;player.Inventory.GetAllItems().Add(Item(40));string id=Start();TerminalTransfer.Ready(7,Reply(id));Check(player.Inventory.GetAllItems().Single().m_stack==50&&Transport.Instance.Results.Last().Success,"partial stack failed");});
  Test("partial insertion failure restores only own additions",()=>{var existing=Item(45);player.Inventory.GetAllItems().Add(existing);player.Inventory.FailAt=2;string id=Start();TerminalTransfer.Ready(7,Reply(id));Check(player.Inventory.GetAllItems().Single()==existing&&existing.m_stack==45&&!Transport.Instance.Results.Last().Success,"partial rollback lost original");});
  Test("throw after insertion is also compensated",()=>{player.Inventory.FailAt=1;player.Inventory.ThrowAfter=true;string id=Start();TerminalTransfer.Ready(7,Reply(id));Check(player.Inventory.GetAllItems().Count==0&&!Transport.Instance.Results.Last().Success,"exception left duplicate");});
  Test("failed local rollback defers remote refund until repaired",()=>{player.Inventory.FailAt=1;player.Inventory.ThrowAfter=true;player.Inventory.FailRestore=true;string id=Start();TerminalTransfer.Ready(7,Reply(id));Check(Transport.Instance.Results.Count==0&&TerminalTransfer.Busy,"refund before cleanup");player.Inventory.FailRestore=false;TerminalTransfer.Ready(7,Reply(id));Check(player.Inventory.GetAllItems().Count==0&&!Transport.Instance.Results.Last().Success,"cleanup retry failed");});
  Test("request retry keeps the same operation ID",()=>{string id=Start();UnityEngine.Time.unscaledTime=3;TerminalTransfer.Tick();Check(Transport.Instance.Requests.Count==2&&Transport.Instance.Requests.All(r=>r.Id==id),"new debit after timeout");});
  Test("quality-specific stock cannot be replaced by other qualities",()=>{StorageIndex.Counts[0].Quality=2;Check(!TerminalTransfer.Start(access,core,player,"Wood",1,10),"wrong quality planned");});
  Test("stand near a relay works far from core",()=>{core.transform.position=new UnityEngine.Vector3{x=80};string id=Start();Check(Transport.Instance.Requests[0].Station==access.Id,"access point not sent");TerminalTransfer.Ready(7,Reply(id));Check(player.Inventory.GetAllItems().Single().m_stack==10,"remote core blocked transfer");});
  Test("standing near the core does not replace access to the stand",()=>{access.transform.position=new UnityEngine.Vector3{x=20};Check(!TerminalTransfer.Start(access,core,player,"Wood",1,10),"core opened storage");});
  Test("lost stand coverage refuses paid parcel",()=>{string id=Start();Topology.Connected=false;TerminalTransfer.Ready(7,Reply(id));Check(player.Inventory.GetAllItems().Count==0&&!Transport.Instance.Results.Last().Success,"disconnected transfer delivered");});
  Test("destroyed stand refuses paid parcel",()=>{string id=Start();access.Valid=false;TerminalTransfer.Tick();TerminalTransfer.Ready(7,Reply(id));Check(player.Inventory.GetAllItems().Count==0&&!Transport.Instance.Results.Last().Success,"destroyed stand delivered");});
  Test("a different open stand cannot receive the old transfer",()=>{string id=Start();NetworkTerminal.ShownAt=new StorageCodex();TerminalTransfer.Ready(7,Reply(id));Check(player.Inventory.GetAllItems().Count==0&&!Transport.Instance.Results.Last().Success,"different window accepted transfer");});
  Test("a ward on the stand blocks access even when core is allowed",()=>{core.transform.position=new UnityEngine.Vector3{x=80};Access.DeniedAt=0;Check(!TerminalTransfer.Start(access,core,player,"Wood",1,10),"stand ward bypass");});
  Test("coordinator checks the stand position rather than the actor for coverage",()=>{RemoteContext.Records[access.Id].Position=new UnityEngine.Vector3{x=4};var op=new Operation{Station=access.Id,PlayerId=1};Check(new RemoteContext(op).TerminalPoint(player.transform.position,out var point)&&point.x==4,"coverage point was actor/core");});
  Test("coordinator accepts synchronized stand without a loaded instance",()=>{var id=access.Id;StorageCodex.Live=null;Check(new RemoteContext(new Operation{Station=id,PlayerId=1}).TerminalPoint(player.transform.position,out _),"host requires loaded stand");});
  Test("coordinator rejects absent or unloaded record",()=>{RemoteContext.Records[access.Id].Valid=false;Check(!new RemoteContext(new Operation{Station=access.Id}).TerminalPoint(player.transform.position,out _),"invalid record accepted");});
  Test("coordinator rejects core or another prefab as an access point",()=>{RemoteContext.Records[access.Id].Prefab=new UnityEngine.GameObject{name="RSN_NetworkCore"};Check(!new RemoteContext(new Operation{Station=access.Id}).TerminalPoint(player.transform.position,out _),"core accepted instead of stand");});
  Test("coordinator rejects an unplaced stand",()=>{RemoteContext.Records[access.Id].Creator=0;Check(!new RemoteContext(new Operation{Station=access.Id}).TerminalPoint(player.transform.position,out _),"unplaced stand accepted");});
  Test("coordinator rejects a distant stand",()=>{RemoteContext.Records[access.Id].Position=new UnityEngine.Vector3{x=10};Check(!new RemoteContext(new Operation{Station=access.Id}).TerminalPoint(player.transform.position,out _),"distant stand accepted");});
  Test("coordinator rejects a ward-protected stand",()=>{Access.DeniedAt=0;Check(!new RemoteContext(new Operation{Station=access.Id}).TerminalPoint(player.transform.position,out _),"remote ward bypass");});
  Console.WriteLine("Terminal runtime tests: "+passed+" passed (stand-ins; no game launched)");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
#endif
