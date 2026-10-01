#if CRAFT_INSPECTION_RUNTIME_TESTS
// Exercise production inspection and reservation display against deterministic
// peers and time. This is not a running Unity/multiplayer test.
#pragma warning disable 0649
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RunicStorageNetwork;
using RunicStorageNetwork.Logic;
namespace UnityEngine {
 public class Object {public static implicit operator bool(Object value)=>value!=null;}
 public static class Time {public static float unscaledTime;}
 public class GameObject:Object {internal Container Container;public T GetComponent<T>() where T:class=>Container as T;}
}
struct ZDOID {public int Value;public static bool operator ==(ZDOID a,ZDOID b)=>a.Value==b.Value;public static bool operator !=(ZDOID a,ZDOID b)=>!(a==b);public override bool Equals(object o)=>o is ZDOID id&&id==this;public override int GetHashCode()=>Value;}
class ZDO {public ZDOID Id;public ZDOID m_uid=>Id;public long Owner=7;public uint DataRevision=1;public byte[] Data=Array.Empty<byte>();public long GetOwner()=>Owner;public int GetInt(int key)=>0;public byte[] GetByteArray(int key)=>Data;}
class ZNetView {public ZDO Zdo;public ZDO GetZDO()=>Zdo;public bool IsOwner()=>Zdo.Owner==ZNet.GetUID();}
static class ZDOVars {public const int s_inUse=1,s_items=2;}
static class ZNet {public static long GetUID()=>3;}
class ZDOMan {public static ZDOMan instance=new ZDOMan();public void RequestZDO(ZDOID id){}}
class Inventory {public List<Stock> Items=new List<Stock>();}
class Container {public ZNetView View;public bool FailRead;public Inventory Inventory=new Inventory();public Inventory GetInventory()=>Inventory;public bool IsInUse()=>false;}
class Recipe:UnityEngine.Object {public object m_resources;}
class Player {}
class ItemDrop {public class ItemData {}}
class ZPackage {
 readonly Queue<object> data=new Queue<object>();
 public void Write(string v)=>data.Enqueue(v);public void Write(int v)=>data.Enqueue(v);public void Write(ZDOID v)=>data.Enqueue(v);
 public void Write(long v)=>data.Enqueue(v);public long ReadLong()=>(long)data.Dequeue();
 public string ReadString()=>(string)data.Dequeue();public int ReadInt()=>(int)data.Dequeue();public ZDOID ReadZDOID()=>(ZDOID)data.Dequeue();
 internal void Add(object v)=>data.Enqueue(v);internal object Take()=>data.Dequeue();
}
class ZNetScene {public static ZNetScene instance=new ZNetScene();public readonly Dictionary<ZDOID,GameObject> Objects=new Dictionary<ZDOID,GameObject>();public GameObject FindInstance(ZDOID id)=>Objects.TryGetValue(id,out var go)?go:null;}
namespace RunicStorageNetwork {
 static class Plugin {internal static void Info(string text){}internal static void Debug(string text){}}
 static class R {internal static string Key(ZDOID id)=>id.Value.ToString();internal static ZNetView View(Container c)=>c.View;internal static object Call(object obj,string method){if(obj is Container c&&c.FailRead)throw new InvalidOperationException("Unreadable saved inventory");return null;}}
 static partial class UnloadedNetworks {static bool ReadFailure(Container c,Exception e)=>c!=null&&c.FailRead;}
 class Core:UnityEngine.Object {internal static List<Core> Live=new List<Core>();internal void Invalidate(){}internal ZDOID Id=new ZDOID{Value=999};internal List<Container> Pool=new List<Container>();}
 class Operation {
  internal string Id=Guid.NewGuid().ToString("N"),Target="recipe";internal bool Build,Quote;internal long Peer=7,PlayerId;internal int Quality=1,Multiplier=1;internal ZDOID Core,Station,Actor;
  internal List<Need> Needs=new List<Need>{new Need("Wood",1)};
  internal bool ReadRequirements(out string why){why=null;return true;}
  internal ZPackage Write(){var p=new ZPackage();p.Add(this);return p;}
  internal static Operation Read(ZPackage p)=>(Operation)p.Take();
 }
 static class Actions {
  internal class Pending {internal Operation Op;internal Recipe Recipe;internal ItemDrop.ItemData Upgrade;internal Player Player;internal List<Debit> Plan;}
  internal static Pending Waiting;
  internal static Operation Create(Player p,Core c,bool build,string target,int quality,int multiplier)=>new Operation{Core=c.Id,Target=target,Quality=quality,Multiplier=multiplier};
  internal static bool Propose(Pending p,List<Debit> d)=>true;
 }
 static class Access {internal static bool Container(Container c,long player,Core core,out string why,bool ownLease){why=null;return true;}}
 static class RemoteData {internal static Dictionary<ZDOID,ZDO> Values=new Dictionary<ZDOID,ZDO>();}
 class RemoteContext {
  internal static bool Readable=true;internal RemoteContext(Operation op){}
  internal static ZDO Data(ZDOID id)=>RemoteData.Values.TryGetValue(id,out var z)?z:null;
  internal static ZDO Source(string key)=>Data(new ZDOID{Value=int.Parse(key)});
  internal static bool Actor(ZDOID id,long sender,long player,out object actor,out string why){actor=null;why=null;return true;}
  internal bool OwnerSource(Container c,out string why,bool ownLease){why="unloaded";return Readable&&c!=null;}
 }
 static class Integrations {internal static bool IsBusy(Inventory inv)=>false;}
 static partial class Stockroom {
  sealed class Observation {internal byte[] Data;internal List<Stock> Items;}
  static readonly Dictionary<string,Observation> observed=new Dictionary<string,Observation>();
  internal static void ClearObservations()=>observed.Clear();
  internal static List<Stock> Snapshot(Inventory i,string key,List<Need> needs,bool chest)=>i.Items.ToList();
  internal static List<Need> Requirements(object resources,int quality,int amount)=>new List<Need>{new Need("Wood",amount)};
 }
 static class Wire {internal static void Stocks(ZPackage p,List<Stock> items)=>p.Add(items);internal static List<Stock> Stocks(ZPackage p)=>(List<Stock>)p.Take();}
 static class Transport {
  internal sealed class Packet {internal long Peer;internal string Method;internal ZPackage Data;}
  internal static List<Packet> Sent=new List<Packet>();internal static bool Locked(Inventory i)=>false;
  internal static void Send(long peer,string method,ZPackage p)=>Sent.Add(new Packet{Peer=peer,Method=method,Data=p});
 }
 static partial class CraftPreparation {
  internal static bool HasReservation,ValidOffer;internal static Actions.Pending offer;
  static bool Ready()=>ValidOffer&&offer!=null;
  static bool Satisfies(Recipe r,List<Need> needs,List<Stock> stock)=>Planner.Plan(needs,stock)!=null;
 }
 static class CraftOverview {
  internal static void Fresh(string key,List<Stock> items){}
  internal static List<Stock> Counts=new List<Stock>();
  internal static List<Stock> Stock(Player p,IEnumerable<Need> needs)=>Counts.ToList();
 }
 static class StorageIndex {
  internal static long Version;
  internal static long Stamp(IEnumerable<Need> needs)=>Version;
  internal static IEnumerable<Container> Candidates(Core core,long player,IEnumerable<Need> needs,bool discover)=>core.Pool;
 }
}
static class CraftInspectionRuntimeTests {
 static int passed;static Core core;static Actions.Pending selection;
 static void Assert(bool value,string message){if(!value)throw new Exception(message);}
 static void Test(string name,Action body){Reset();body();passed++;Console.WriteLine("PASS inspection runtime "+name);}
 static void Reset(int count=1){
  Time.unscaledTime=0;StorageIndex.Version=0;CraftInspection.Clear();CraftPreparation.HasReservation=CraftPreparation.ValidOffer=false;CraftPreparation.offer=null;CraftOverview.Counts.Clear();Actions.Waiting=null;
  Transport.Sent.Clear();RemoteData.Values.Clear();Stockroom.ClearObservations();ZNetScene.instance.Objects.Clear();RemoteContext.Readable=true;core=new Core();
  selection=new Actions.Pending{Player=new Player(),Recipe=new Recipe(),Op=new Operation()};
  for(int i=1;i<=count;i++){var id=new ZDOID{Value=i};var z=new ZDO{Id=id};var c=new Container{View=new ZNetView{Zdo=z}};core.Pool.Add(c);RemoteData.Values[id]=z;ZNetScene.instance.Objects[id]=new GameObject{Container=c};}
  Stockroom.Observe("1",new List<Stock>{new Stock("1","Wood",1,50)});CraftInspection.Ensure(selection,core);
 }
 static Operation Request(){CraftInspection.Tick();return Operation.Read(Transport.Sent.Last().Data);}
 static void Reply(Operation op,int amount,long peer=7,string source="1"){
  var p=new ZPackage();p.Write(op.Id);p.Write(source);p.Write(1L);Wire.Stocks(p,amount<0?new List<Stock>():new List<Stock>{new Stock(source,"Wood",1,amount)});CraftInspection.Response(peer,p);
 }
 static int Count()=>Stockroom.Preview(core.Pool[0]).Sum(s=>s.Amount);
 public static int Main(){try{
  Test("lost reply preserves counts and allows owner-validated preparation",()=>{Request();Time.unscaledTime=7;CraftInspection.Tick();Assert(Count()==50&&CraftInspection.Ready,"timeout fabricated empty inventory or blocked progress");});
  Test("confirmed empty inventory clears a previously available ingredient",()=>{var op=Request();Reply(op,-1);Assert(Count()==0&&CraftInspection.Ready,"real zero ignored");});
  Test("another player's consumption replaces the old total",()=>{var op=Request();Reply(op,3);Assert(Count()==3,"old total survived confirmed consumption");});
  Test("delayed older revision cannot overwrite newer synchronized inventory",()=>{var op=Request();core.Pool[0].View.Zdo.DataRevision=2;Reply(op,3);Assert(Count()==50,"older inventory revision accepted");});
  Test("owner's changed live inventory wins over an old observation",()=>{core.Pool[0].View.Zdo.Owner=3;core.Pool[0].Inventory.Items.Add(new Stock("1","Wood",1,8));Assert(Count()==8,"old observation hid local mutation");});
  Test("late reply can correct a deferred source before next refresh",()=>{var op=Request();Time.unscaledTime=7;CraftInspection.Tick();Reply(op,9);Assert(Count()==9,"late current reply discarded");});
  Test("reply from former owner cannot erase stock",()=>{var op=Request();core.Pool[0].View.Zdo.Owner=8;Reply(op,-1);Assert(Count()==50,"former owner accepted");});
  Test("ownership change sends next request to the new owner",()=>{Request();core.Pool[0].View.Zdo.Owner=8;Time.unscaledTime=1;CraftInspection.Tick();Assert(Transport.Sent.Last().Peer==8,"new owner not queried");});
  Test("old selection reply cannot overwrite new selection",()=>{var op=Request();selection.Recipe=new Recipe();CraftInspection.Ensure(selection,core);Reply(op,-1);Assert(Count()==50,"old selection accepted");});
  Test("unreadable source sends no false empty result",()=>{RemoteContext.Readable=false;var p=selection.Op.Write();p.Write(1);p.Write(core.Pool[0].View.Zdo.Id);CraftInspection.Request(7,p);Assert(Transport.Sent.Count==0,"unreadable source reported empty");});
  Test("readable empty source sends a confirmed empty snapshot",()=>{var p=selection.Op.Write();p.Write(1);p.Write(core.Pool[0].View.Zdo.Id);CraftInspection.Request(7,p);Assert(Transport.Sent.Count==1&&Transport.Sent[0].Method=="inspected","empty reply missing");});
  Test("bad unloaded chest does not abort healthy chests in same RPC",()=>{Reset(3);core.Pool[0].FailRead=true;var p=selection.Op.Write();p.Write(3);foreach(var c in core.Pool)p.Write(c.View.Zdo.Id);CraftInspection.Request(7,p);Assert(Transport.Sent.Count==2&&Transport.Sent.All(s=>s.Method=="inspected"),"healthy replies discarded or bad chest reported empty");foreach(var reply in Transport.Sent){reply.Data.ReadString();Assert(reply.Data.ReadString()!="1","false snapshot for failed chest");}});
  Test("inspection pauses while recipe is reserved",()=>{CraftPreparation.HasReservation=true;CraftInspection.Tick();Assert(Transport.Sent.Count==0,"queried during reservation");});
  Test("inspection pauses while payment is executing",()=>{Actions.Waiting=selection;CraftInspection.Tick();Assert(Transport.Sent.Count==0,"queried during payment");});
  Test("in-flight reply cannot change counts during a reservation",()=>{var op=Request();CraftPreparation.HasReservation=true;Reply(op,-1);Assert(Count()==50,"reservation display overwritten");});
  Test("unchanged recipe stock sends no periodic owner queries",()=>{var op=Request();Reply(op,50);int sent=Transport.Sent.Count;Time.unscaledTime=20;CraftInspection.Ensure(selection,core);CraftInspection.Tick();Assert(Transport.Sent.Count==sent,"unnecessary refresh");StorageIndex.Version++;CraftInspection.Ensure(selection,core);CraftInspection.Tick();Assert(Transport.Sent.Count==sent+1,"changed stock never refreshed");});
  Test("95 chests are inspected with at most 16 per packet",()=>{Reset(95);for(int i=0;i<6;i++)CraftInspection.Tick();int count=0;foreach(var packet in Transport.Sent){Operation.Read(packet.Data);int size=packet.Data.ReadInt();Assert(size<=16,"oversized packet");count+=size;}Assert(count==95,"some chests skipped");});
  Test("one unavailable chest does not erase successful replies",()=>{Reset(2);var op=Request();Reply(op,12);Time.unscaledTime=7;CraftInspection.Tick();Assert(Count()==12&&CraftInspection.Ready,"partial refresh discarded");});
  Test("reservation floor does not double-count shared stock",()=>{var stock=new[]{new Stock("1","Wood",1,50)};var plan=new[]{new Debit("1","Wood",1,10)};Assert(ReservationStock.Merge(stock,plan,new[]{new Need("Wood",10)}).Sum(s=>s.Amount)==50,"reservation counted twice");});
  Test("confirmed reservation survives stale zero in display",()=>{var stock=new[]{new Stock("1","Wood",1,0)};var plan=new[]{new Debit("1","Wood",1,10)};Assert(ReservationStock.Merge(stock,plan,new[]{new Need("Wood",10)}).Sum(s=>s.Amount)==10&&stock[0].Amount==0,"reservation lost or input mutated");});
  Test("reservation keeps source and quality boundaries",()=>{var stock=new[]{new Stock("2","Wood",2,7)};var plan=new[]{new Debit("1","Wood",1,10),new Debit("1","Stone",1,5)};var result=ReservationStock.Merge(stock,plan,new[]{new Need("Wood",10)});Assert(result.Count==2&&result.Sum(s=>s.Amount)==17&&!result.Any(s=>s.Item=="Stone"),"stock scope mixed");});
  Test("selected confirmed recipe remains available despite stale overview",()=>{CraftPreparation.offer=selection;CraftPreparation.ValidOffer=true;Assert(CraftPreparation.Available(selection.Player,selection.Recipe,1,1),"confirmed reserve hidden by stale overview");});
  Test("reservation does not authorize another recipe or quantity",()=>{CraftPreparation.offer=selection;CraftPreparation.ValidOffer=true;Assert(!CraftPreparation.Available(selection.Player,new Recipe(),1,1)&&!CraftPreparation.Available(selection.Player,selection.Recipe,1,5)&&!CraftPreparation.Available(new Player(),selection.Recipe,1,1),"reservation escaped its intent");});
  Test("expired reservation cannot enable empty recipe",()=>{CraftPreparation.offer=selection;CraftPreparation.ValidOffer=false;Assert(!CraftPreparation.Available(selection.Player,selection.Recipe,1,1),"expired reservation still authoritative");});
  Test("display uses confirmed plan and drops floor after expiry",()=>{selection.Plan=new List<Debit>{new Debit("1","Wood",1,10)};CraftPreparation.offer=selection;CraftPreparation.ValidOffer=true;var needs=new[]{new Need("Wood",10)};Assert(CraftPreparation.Stock(selection.Player,needs).Sum(s=>s.Amount)==10,"reserve missing from requirement label");CraftPreparation.ValidOffer=false;Assert(CraftPreparation.Stock(selection.Player,needs).Count==0,"expired reserve left in label");});
  Test("elapsed time does not replace owner-confirmed totals with stale local inventory",()=>{Time.unscaledTime=60;Assert(Count()==50,"older empty local snapshot replaced confirmed stock");});
  Test("changed synchronized inventory supersedes previous owner snapshot",()=>{core.Pool[0].View.Zdo.Data=new byte[]{1};core.Pool[0].Inventory.Items.Add(new Stock("1","Wood",1,2));Assert(Count()==2,"old confirmation masked synchronized consumption");});
  Test("confirmed zero is not replaced by stale positive local stock after ten seconds",()=>{var op=Request();Reply(op,-1);core.Pool[0].Inventory.Items.Add(new Stock("1","Wood",1,50));Time.unscaledTime=60;Assert(Count()==0,"consumed items reappeared");});
  Console.WriteLine("Inspection runtime tests: "+passed+" passed");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
#endif
